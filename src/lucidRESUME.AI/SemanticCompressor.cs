using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Semantic resume compression. Given a JD's skill requirements, queries the
/// skill ledger for evidence of each required skill, selects only the most
/// relevant roles and achievements, and composes a compressed resume.
///
/// A 30-year career with 13 roles becomes 2 pages of laser-targeted evidence.
///
/// The result retains stable ledger claim IDs and is rendered without re-inference.
/// </summary>
public sealed class SemanticCompressor
{
    private static readonly HashSet<string> GenericSkillLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "engineering", "systems", "system", "technical", "tech", "technology", "technologies",
        "performance", "lead", "leading", "management", "it", "software", "development"
    };

    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly JdSkillLedgerBuilder _jdLedgerBuilder;
    private readonly SkillLedgerMatcher _matcher;
    private readonly IEmbeddingService _embedder;
    private readonly ILogger<SemanticCompressor> _logger;

    public SemanticCompressor(
        SkillLedgerBuilder ledgerBuilder,
        JdSkillLedgerBuilder jdLedgerBuilder,
        SkillLedgerMatcher matcher,
        IEmbeddingService embedder,
        ILogger<SemanticCompressor> logger)
    {
        _ledgerBuilder = ledgerBuilder;
        _jdLedgerBuilder = jdLedgerBuilder;
        _matcher = matcher;
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Compress a resume to only the evidence that matches a JD's requirements.
    /// Returns a pre-filtered projection whose prose blocks remain bound to ledger claims.
    /// </summary>
    public async Task<CompressedResume> CompressAsync(
        ResumeDocument resume, JobDescription jd, CancellationToken ct = default)
    {
        var resumeLedger = await _ledgerBuilder.BuildAsync(resume, ct);
        var jdLedger = await _jdLedgerBuilder.BuildAsync(jd, ct);
        var matchResult = await _matcher.MatchAsync(resumeLedger, jdLedger, ct);

        _logger.LogInformation(
            "Compressing resume for {Title} at {Company}: fit={Fit:P0}, {Matched}/{Total} skills matched",
            jd.Title, jd.Company, matchResult.OverallFit,
            matchResult.Matches.Count(m => m.IsMatched), matchResult.Matches.Count);

        // Collect the most relevant experience IDs - roles that evidence required skills
        var relevantRoleIds = new HashSet<Guid>();
        var skillToEvidence = new Dictionary<string, List<SkillEvidence>>();

        foreach (var match in matchResult.Matches.Where(m => m.IsMatched))
        {
            var ledgerEntry = resumeLedger.Find(match.MatchedResumeSkill!);
            if (ledgerEntry is null) continue;

            skillToEvidence[match.RequiredSkill] = ledgerEntry.Evidence
                .OrderByDescending(e => e.Confidence)
                .ThenByDescending(e => e.StartDate)
                .Take(3) // top 3 evidence pieces per skill
                .ToList();

            foreach (var evidence in skillToEvidence[match.RequiredSkill])
            {
                if (evidence.ExperienceId.HasValue)
                    relevantRoleIds.Add(evidence.ExperienceId.Value);
            }
        }

        // Always include current role and most recent 2 roles (even if no direct skill match)
        var recentRoles = resume.Experience
            .OrderByDescending(e => e.StartDate)
            .Take(3)
            .Select(e => e.Id);
        foreach (var id in recentRoles)
            relevantRoleIds.Add(id);

        var sourceLedger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        var projection = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        projection.Personal = new PersonalInfo
        {
            FullName = resume.Personal.FullName,
            Email = resume.Personal.Email,
            Phone = resume.Personal.Phone,
            Location = resume.Personal.Location,
            LinkedInUrl = resume.Personal.LinkedInUrl,
            GitHubUrl = resume.Personal.GitHubUrl,
            WebsiteUrl = resume.Personal.WebsiteUrl,
            Summary = resume.Personal.Summary
        };
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = resume.ResumeId,
            SourceRevision = sourceLedger.SourceRevision
        };

        // Build Markdown and its ledger bindings together. This is a projection, not inference.
        var md = new StringBuilder();

        // Header
        md.AppendLine($"# {resume.Personal.FullName ?? ""}");
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(resume.Personal.Email)) contactParts.Add(resume.Personal.Email);
        if (!string.IsNullOrEmpty(resume.Personal.Phone)) contactParts.Add(resume.Personal.Phone);
        if (!string.IsNullOrEmpty(resume.Personal.GitHubUrl)) contactParts.Add(resume.Personal.GitHubUrl);
        if (contactParts.Count > 0)
        {
            md.AppendLine(string.Join(" | ", contactParts));
            foreach (var field in new[] { "email", "phone", "github" })
                Bind(projection, sourceLedger, $"personal:{field}", "#document:p1");
        }
        md.AppendLine();

        // Compressed summary - targeted to the JD
        if (!string.IsNullOrWhiteSpace(resume.Personal.Summary))
        {
            md.AppendLine("## Summary {#summary}");
            md.AppendLine(resume.Personal.Summary);
            md.AppendLine();
            Bind(projection, sourceLedger, "personal:summary", "#summary:p1");
        }

        // Relevant experience only
        md.AppendLine("## Experience {#experience}");
        md.AppendLine();
        var includedRoles = 0;
        foreach (var exp in resume.Experience.OrderByDescending(e => e.StartDate))
        {
            if (!relevantRoleIds.Contains(exp.Id) && includedRoles >= 3)
                continue; // skip non-relevant roles after we have 3

            md.AppendLine($"### {exp.Title ?? ""} - {exp.Company ?? ""} {{#experience-{exp.Id:N}}}");
            var dateRange = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
            if (!string.IsNullOrEmpty(dateRange))
            {
                md.AppendLine($"*{dateRange}*");
                md.AppendLine();
            }

            // Include only achievements that evidence matched skills
            var relevantAchievements = new List<string>();
            foreach (var achievement in exp.Achievements)
            {
                // Check if this achievement is evidence for any matched skill
                var isEvidence = skillToEvidence.Values
                    .SelectMany(e => e)
                    .Any(e => e.SourceText == achievement);

                if ((isEvidence || relevantAchievements.Count < 2) &&
                    relevantAchievements.All(existing => !NearDuplicate(existing, achievement)))
                    relevantAchievements.Add(achievement);
            }

            var projectedExperience = new WorkExperience
            {
                Id = exp.Id,
                Company = exp.Company,
                Title = exp.Title,
                Location = exp.Location,
                StartDate = exp.StartDate,
                EndDate = exp.EndDate,
                IsCurrent = exp.IsCurrent,
                Technologies = [.. exp.Technologies],
                ImportSources = [.. exp.ImportSources]
            };
            var paragraph = string.IsNullOrEmpty(dateRange) ? 1 : 2;
            foreach (var a in relevantAchievements.Take(5)) // cap at 5 per role
            {
                md.AppendLine($"- {a}");
                md.AppendLine();
                projectedExperience.Achievements.Add(a);
                var sourceIndex = exp.Achievements.IndexOf(a);
                if (sourceIndex >= 0)
                    Bind(projection, sourceLedger, $"experience:{exp.Id:N}:achievement:{sourceIndex + 1}",
                        $"#experience-{exp.Id:N}:p{paragraph++}");
            }
            projection.Experience.Add(projectedExperience);
            includedRoles++;
        }

        // Project only ledger-backed, specific skills. Concepts demonstrated by the
        // selected prose are included even when the JD matcher chose a broader alias.
        md.AppendLine("## Skills {#skills}");
        var matchedSkillNames = matchResult.Matches
            .Where(m => m.IsMatched)
            .Select(m => resumeLedger.Find(m.MatchedResumeSkill!))
            .Where(e => e is not null)
            .Select(entry => entry!.SkillName);
        var demonstratedConcepts = projection.Projection.Blocks
            .Select(block => sourceLedger.Claims.FirstOrDefault(claim => claim.Id == block.ClaimId))
            .Where(claim => claim is not null)
            .SelectMany(claim => claim!.Concepts);
        var skillsByCategory = matchedSkillNames.Concat(demonstratedConcepts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !GenericSkillLabels.Contains(name))
            .Select(name => resume.Skills.FirstOrDefault(skill =>
                string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Where(skill => skill is not null)
            .DistinctBy(skill => skill!.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(skill => skill!.Category ?? "General")
            .ToList();

        var skillParagraph = 1;
        foreach (var group in skillsByCategory)
        {
            var entries = group.ToList();
            md.AppendLine($"**{group.Key}:** {string.Join(", ", entries.Select(skill => skill!.Name))}");
            md.AppendLine();
            foreach (var entry in entries)
            {
                projection.Skills.Add(new Skill
                {
                    Name = entry!.Name,
                    Category = entry.Category,
                    YearsExperience = entry.YearsExperience,
                    ImportSources = [.. entry.ImportSources]
                });
                Bind(projection, sourceLedger, EvidenceLedgerBuilder.SkillLocator(entry.Name), $"#skills:p{skillParagraph}");
            }
            skillParagraph++;
        }

        // Education
        if (resume.Education.Count > 0)
        {
            md.AppendLine("## Education {#education}");
            var educationParagraph = 1;
            foreach (var edu in resume.Education)
            {
                var qualification = string.IsNullOrWhiteSpace(edu.FieldOfStudy)
                    ? edu.Degree ?? ""
                    : $"{edu.Degree} | {edu.FieldOfStudy}";
                md.AppendLine($"**{qualification}** | {edu.Institution ?? ""}");
                md.AppendLine();
                projection.Education.Add(edu);
                Bind(projection, sourceLedger, $"education:{edu.Id:N}", $"#education:p{educationParagraph++}");
            }
        }

        var compressedMd = md.ToString();
        projection.SetDoclingOutput(compressedMd, null, compressedMd);
        projection.EvidenceLedger = sourceLedger;

        return new CompressedResume
        {
            Markdown = compressedMd,
            OriginalRoleCount = resume.Experience.Count,
            IncludedRoleCount = includedRoles,
            OriginalSkillCount = resume.Skills.Count,
            MatchedSkillCount = matchResult.Matches.Count(m => m.IsMatched),
            OverallFit = matchResult.OverallFit,
            Gaps = matchResult.Gaps,
            Projection = projection
        };
    }

    private static void Bind(ResumeDocument projection, EvidenceLedger ledger, string locator, string proseRef)
    {
        var evidence = ledger.Evidence.FirstOrDefault(item =>
            string.Equals(item.Locator, locator, StringComparison.OrdinalIgnoreCase));
        if (evidence is null) return;
        var claim = ledger.Claims.FirstOrDefault(item => item.EvidenceIds.Contains(evidence.Id, StringComparer.OrdinalIgnoreCase));
        if (claim is null) return;
        projection.Projection!.Blocks.Add(new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = proseRef });
    }

    private static bool NearDuplicate(string left, string right)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;
        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union > 0 && intersection / (double)union >= 0.25;
    }

    private static HashSet<string> Tokens(string text) => text.ToLowerInvariant()
        .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '(', ')', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length > 2)
        .Select(token => token.Length > 7 ? token[..6] : token.TrimEnd('s'))
        .ToHashSet(StringComparer.Ordinal);

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        if (s == "" && e == "") return "";
        return e == "" ? s : s == "" ? e : $"{s} – {e}";
    }
}

public sealed class CompressedResume
{
    public string Markdown { get; init; } = "";
    public int OriginalRoleCount { get; init; }
    public int IncludedRoleCount { get; init; }
    public int OriginalSkillCount { get; init; }
    public int MatchedSkillCount { get; init; }
    public double OverallFit { get; init; }
    public List<string> Gaps { get; init; } = [];
    public ResumeDocument Projection { get; init; } = new();
}
