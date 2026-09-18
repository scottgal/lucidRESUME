using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;

namespace lucidRESUME.Ingestion;

/// <summary>
/// Parses and merges a directory of resume sources into one provenance-aware evidence corpus.
/// The most information-rich source becomes the base; every source remains available in the
/// raw Markdown corpus so generation does not discard evidence merely because parsers disagree.
/// </summary>
public sealed class ResumeCorpusLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pdf", ".txt", ".md", ".markdown"
    };

    private readonly IResumeParser _parser;
    private readonly IEmbeddingService _embedder;

    public ResumeCorpusLoader(IResumeParser parser, IEmbeddingService embedder)
    {
        _parser = parser;
        _embedder = embedder;
    }

    public async Task<ResumeCorpus> LoadDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        var directory = new DirectoryInfo(directoryPath);
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"Resume directory not found: {directory.FullName}");

        var files = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => SupportedExtensions.Contains(file.Extension))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"No supported resume files found in {directory.FullName}.");

        var parsed = new List<ResumeDocument>(files.Count);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            parsed.Add(await ParseAsync(file, ct));
        }

        var ordered = parsed.OrderByDescending(InformationScore).ToList();
        var merged = ResumeDocument.Create($"{directory.Name}-merged.md", "text/markdown", 0);
        var merger = new ResumeDocumentMerger(_embedder);
        var anomalies = new List<ImportAnomaly>();
        var structuredSources = new List<string>();
        foreach (var incoming in ordered)
        {
            var projection = StructuredProjection(incoming);
            if (projection.Experience.Count > 0 || projection.Skills.Count > 0 ||
                projection.Projects.Count > 0 || projection.Education.Count > 0)
            {
                structuredSources.Add(incoming.FileName);
                anomalies.AddRange(await merger.MergeIntoAsync(merged, projection, incoming.FileName, ct));
            }
        }

        CollapseGenericConsultingRoles(merged);
        ResolvePersonalConsensus(merged.Personal, parsed.Select(document => document.Personal));

        var corpusMarkdown = BuildCorpusMarkdown(parsed);
        FillContactFromCorpus(merged.Personal, corpusMarkdown);
        merged.RawMarkdown = corpusMarkdown;
        merged.PlainText = corpusMarkdown;
        merged.FileName = $"{directory.Name}-merged.md";
        merged.ContentType = "text/markdown";
        merged.FileSizeBytes = Encoding.UTF8.GetByteCount(corpusMarkdown);
        EvidenceLedgerBuilder.RebuildFromSources(merged,
            parsed.Select(document => EvidenceLedgerBuilder.EnsureCurrent(document)));

        return new ResumeCorpus(merged, parsed, anomalies, structuredSources);
    }

    private async Task<ResumeDocument> ParseAsync(FileInfo file, CancellationToken ct)
    {
        if (file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            file.Extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            var markdown = await File.ReadAllTextAsync(file.FullName, ct);
            var resume = ResumeDocument.Create(file.Name, "text/markdown", file.Length);
            resume.SetDoclingOutput(markdown, null, markdown);
            MarkdownSectionParser.PopulateSections(resume, markdown);
            return resume;
        }

        var parsed = await _parser.ParseAsync(file.FullName, ct);
        if (parsed.LlmEnhancementTask is not null)
        {
            try { await parsed.LlmEnhancementTask; }
            catch { /* Enhancement is optional; deterministic extraction remains evidence. */ }
        }
        return parsed;
    }

    private static int InformationScore(ResumeDocument resume) =>
        resume.Experience.Count * 20
        + resume.Experience.Sum(item => item.Achievements.Count) * 4
        + resume.Skills.Count * 2
        + resume.Projects.Count * 5
        + resume.Education.Count * 5
        + (resume.Personal.FullName is null ? 0 : 10);

    private static void CollapseGenericConsultingRoles(ResumeDocument resume)
    {
        string[] genericCompanies = ["consulting", "freelance", "self employed", "self-employed"];
        foreach (var generic in resume.Experience
                     .Where(item => genericCompanies.Contains(item.Company ?? "", StringComparer.OrdinalIgnoreCase))
                     .ToList())
        {
            var named = resume.Experience.FirstOrDefault(item => !ReferenceEquals(item, generic)
                && item.IsCurrent == generic.IsCurrent
                && item.StartDate.HasValue && generic.StartDate.HasValue
                && Math.Abs(item.StartDate.Value.DayNumber - generic.StartDate.Value.DayNumber) <= 90
                && SharedTitleToken(item.Title, generic.Title));
            if (named is null) continue;
            foreach (var achievement in generic.Achievements)
                if (!named.Achievements.Contains(achievement, StringComparer.OrdinalIgnoreCase)) named.Achievements.Add(achievement);
            foreach (var source in generic.ImportSources)
                if (!named.ImportSources.Contains(source, StringComparer.OrdinalIgnoreCase)) named.ImportSources.Add(source);
            resume.Experience.Remove(generic);
        }
    }

    private static bool SharedTitleToken(string? first, string? second)
    {
        var firstTokens = (first ?? "").Split([' ', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 5).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (second ?? "").Split([' ', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(firstTokens.Contains);
    }

    private static ResumeDocument StructuredProjection(ResumeDocument source)
    {
        var json = JsonSerializer.Serialize(source);
        var projection = JsonSerializer.Deserialize<ResumeDocument>(json)!;
        projection.Experience = projection.Experience.Where(IsPlausibleExperience).ToList();
        projection.Skills = projection.Skills.SelectMany(NormalizeSkill).DistinctBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase).ToList();
        projection.Education = projection.Education.Select(NormalizeEducation).Where(item => item is not null).Cast<Education>().ToList();
        if (!IsPlausibleName(projection.Personal.FullName)) projection.Personal.FullName = null;
        return projection;
    }

    private static bool IsPlausibleExperience(WorkExperience experience)
    {
        if (string.IsNullOrWhiteSpace(experience.Company) || string.IsNullOrWhiteSpace(experience.Title)) return false;
        var company = experience.Company.Trim();
        if (company.Length <= 3 || company.Equals("Redmond", StringComparison.OrdinalIgnoreCase)) return false;
        if (experience.StartDate.HasValue && experience.EndDate.HasValue && experience.EndDate < experience.StartDate) return false;
        return experience.Title.Trim('*', '#', ' ').Length >= 3;
    }

    private static IEnumerable<Skill> NormalizeSkill(Skill skill)
    {
        var value = skill.Name.Trim();
        value = Regex.Replace(value, @"\s*\((?:\d+\s+years?|and many more)\)\s*$", "", RegexOptions.IgnoreCase);
        var replacements = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["C# JavaScript"] = ["C#", "JavaScript"],
            ["ASP.NET to .NET 8"] = ["ASP.NET", ".NET 8"],
            ["CI & CD using GH Actions"] = ["CI/CD", "GitHub Actions"],
            ["Azure and Bicep etc"] = ["Azure", "Bicep"],
            ["NoSQL (MongoDB"] = ["NoSQL", "MongoDB"],
            ["SQL DBs"] = ["SQL"],
            ["NoSQL DBs"] = ["NoSQL"],
            ["Postgres"] = ["PostgreSQL"],
            ["Node"] = ["Node.js"],
            ["Vue"] = ["Vue.js"]
        };
        if (replacements.TryGetValue(value, out var replacementValues))
        {
            foreach (var replacement in replacementValues)
                yield return CopySkill(skill, replacement);
            yield break;
        }
        if (value.StartsWith("NoSQL DBs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var replacement in new[] { "NoSQL", "MongoDB", "PostgreSQL" })
                yield return CopySkill(skill, replacement);
            yield break;
        }
        if (value.Equals("Postgres)", StringComparison.OrdinalIgnoreCase))
        {
            yield return CopySkill(skill, "PostgreSQL");
            yield break;
        }
        if (value.StartsWith("UDP ", StringComparison.OrdinalIgnoreCase))
        {
            yield return CopySkill(skill, "UDP");
            yield break;
        }

        string[] excluded =
        [
            "Manual", "Dell", "ecommerce", "Hiking", "Reading tech blogs", "Traveling",
            "Mentoring aspiring developers", "Lead Developer", "Senior .NET Developer",
            "Development Lead", "Head of Engineering", "CTO", "Led globally distributed teams"
        ];
        string[] sentenceMarkers = ["delivered ", "including ", "driving ", "and high", "in various", "scott galloway"];
        if (value.Length is < 2 or > 50 || value.Count(char.IsWhiteSpace) > 5 || value.EndsWith('.') ||
            excluded.Contains(value, StringComparer.OrdinalIgnoreCase) ||
            sentenceMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            yield break;
        yield return CopySkill(skill, value);
    }

    private static Skill CopySkill(Skill source, string name) => new()
    {
        Name = name,
        Category = source.Category,
        YearsExperience = source.YearsExperience,
        EndorsementCount = source.EndorsementCount,
        ImportSources = [.. source.ImportSources]
    };

    private static Education? NormalizeEducation(Education education)
    {
        var institution = education.Institution?.Trim();
        var degree = education.Degree?.Trim();
        var field = education.FieldOfStudy?.Trim();

        if (institution?.StartsWith("University of Stirling,", StringComparison.OrdinalIgnoreCase) == true)
        {
            degree ??= institution[(institution.IndexOf(',') + 1)..].Trim();
            institution = "University of Stirling";
        }
        if (string.IsNullOrWhiteSpace(institution) && field?.Contains("University of Stirling", StringComparison.OrdinalIgnoreCase) == true)
        {
            institution = "University of Stirling";
            field = null;
        }

        var recognisedInstitution = institution?.Contains("university", StringComparison.OrdinalIgnoreCase) == true ||
                                    institution?.Contains("college", StringComparison.OrdinalIgnoreCase) == true;
        var recognisedDegree = degree is not null && Regex.IsMatch(degree, @"(?i)\b(BSc|BA|MSc|MA|PhD|degree|diploma)\b");
        if (!recognisedInstitution && !recognisedDegree) return null;
        if ((degree?.Length ?? 0) > 100 || (field?.Length ?? 0) > 100) return null;

        education.Institution = institution;
        education.Degree = degree;
        education.FieldOfStudy = field;
        return education;
    }

    private static void ResolvePersonalConsensus(PersonalInfo target, IEnumerable<PersonalInfo> candidates)
    {
        var values = candidates.ToList();
        target.FullName = MostCommon(values.Select(item => item.FullName).Where(IsPlausibleName));
        target.Email = MostCommon(values.Select(item => item.Email).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.Phone = MostCommon(values.Select(item => item.Phone).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.Location = MostCommon(values.Select(item => item.Location).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.LinkedInUrl = MostCommon(values.Select(item => item.LinkedInUrl).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.GitHubUrl = MostCommon(values.Select(item => item.GitHubUrl).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.WebsiteUrl = MostCommon(values.Select(item => item.WebsiteUrl).Where(value => !string.IsNullOrWhiteSpace(value))!);
        target.Summary = values.Select(item => item.Summary).Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value!.Length).FirstOrDefault();
    }

    private static bool IsPlausibleName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 5) return false;
        string[] headings = ["professional", "profile", "summary", "highlight", "experience", "curriculum", "resume"];
        return headings.All(heading => !value.Contains(heading, StringComparison.OrdinalIgnoreCase));
    }

    private static string? MostCommon(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .GroupBy(value => value!.Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .ThenByDescending(group => group.Key.Length)
        .Select(group => group.Key)
        .FirstOrDefault();

    private static void FillContactFromCorpus(PersonalInfo personal, string corpus)
    {
        personal.Email ??= Regex.Match(corpus, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b").Value;
        personal.LinkedInUrl ??= Regex.Match(corpus, @"https?://(?:www\.)?linkedin\.com/in/[^\s)]+", RegexOptions.IgnoreCase).Value;
        personal.GitHubUrl ??= Regex.Match(corpus, @"https?://(?:www\.)?github\.com/[^\s)]+", RegexOptions.IgnoreCase).Value;
        personal.WebsiteUrl ??= Regex.Matches(corpus, @"https?://[^\s)]+", RegexOptions.IgnoreCase)
            .Select(match => match.Value.TrimEnd('.', ',', ';'))
            .FirstOrDefault(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                !uri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase));
        personal.Location ??= Regex.Match(corpus, @"(?im)^Location:\s*(?<location>[^\r\n]+)$").Groups["location"].Value.Trim();
        if (string.IsNullOrWhiteSpace(personal.Email)) personal.Email = null;
        if (string.IsNullOrWhiteSpace(personal.LinkedInUrl)) personal.LinkedInUrl = null;
        if (string.IsNullOrWhiteSpace(personal.GitHubUrl)) personal.GitHubUrl = null;
        if (string.IsNullOrWhiteSpace(personal.WebsiteUrl)) personal.WebsiteUrl = null;

    }

    private static string BuildCorpusMarkdown(IEnumerable<ResumeDocument> documents)
    {
        var builder = new StringBuilder("# Imported Resume Evidence Corpus\n");
        foreach (var document in documents)
        {
            var content = document.RawMarkdown ?? document.PlainText;
            if (string.IsNullOrWhiteSpace(content)) continue;
            builder.Append("\n\n<!-- source: ")
                .Append(document.FileName.Replace("--", "-", StringComparison.Ordinal))
                .Append(" -->\n\n")
                .Append(content.Trim());
        }
        return builder.AppendLine().ToString();
    }
}

public sealed record ResumeCorpus(
    ResumeDocument Merged,
    IReadOnlyList<ResumeDocument> Sources,
    IReadOnlyList<ImportAnomaly> Anomalies,
    IReadOnlyList<string> StructuredSources);
