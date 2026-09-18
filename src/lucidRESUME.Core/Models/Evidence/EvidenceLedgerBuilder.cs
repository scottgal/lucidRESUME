using System.Text;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Models.Evidence;

/// <summary>Creates and refreshes the evidence ledger from ingestion results.</summary>
public static partial class EvidenceLedgerBuilder
{
    public static EvidenceLedger EnsureCurrent(ResumeDocument resume)
    {
        var revision = ComputeSourceRevision(resume);
        if (resume.EvidenceLedger.SourceRevision == revision && resume.EvidenceLedger.Evidence.Count > 0)
            return resume.EvidenceLedger;
        return Rebuild(resume, revision);
    }

    public static EvidenceLedger Rebuild(ResumeDocument resume) => Rebuild(resume, ComputeSourceRevision(resume));

    /// <summary>
    /// Rebuilds the merged document's claims while retaining the original evidence records
    /// from every imported document. Equivalent source passages become multiple supports for
    /// one merged claim instead of being flattened into an anonymous aggregate.
    /// </summary>
    public static EvidenceLedger RebuildFromSources(ResumeDocument resume, IEnumerable<EvidenceLedger> sources)
    {
        var sourceEvidence = sources.SelectMany(source => source.Evidence)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var ledger = Rebuild(resume);
        foreach (var claim in ledger.Claims)
        {
            var aggregateEvidence = claim.EvidenceIds
                .Select(id => ledger.Evidence.First(item => item.Id == id))
                .First();
            var originals = sourceEvidence.Where(item =>
                    string.Equals(item.Kind, aggregateEvidence.Kind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Normalize(item.Text), Normalize(aggregateEvidence.Text), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (originals.Count == 0) continue;
            claim.EvidenceIds = originals.Select(item => item.Id).ToList();
            if (originals.All(item => item.ExtractionMethod is "ner" or "llm" or "extracted"))
            {
                claim.Origin = "extracted";
                claim.Review = "required";
            }
            ledger.Evidence.Remove(aggregateEvidence);
            foreach (var original in originals)
                if (ledger.Evidence.All(item => !string.Equals(item.Id, original.Id, StringComparison.OrdinalIgnoreCase)))
                    ledger.Evidence.Add(original);
        }
        resume.EvidenceLedger = ledger;
        return ledger;
    }

    public static string FastHash(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var octet in Encoding.UTF8.GetBytes(Normalize(value)))
        {
            hash ^= octet;
            hash *= prime;
        }
        return $"fnv1a64:{hash:x16}";
    }

    private static EvidenceLedger Rebuild(ResumeDocument resume, string revision)
    {
        var ledger = new EvidenceLedger { SourceRevision = revision, BuiltAt = DateTimeOffset.UtcNow };

        AddPersonal(ledger, resume, "name", resume.Personal.FullName,
            WasLlmExtracted(resume, resume.Personal.FullName, "PersonName"));
        AddPersonal(ledger, resume, "email", resume.Personal.Email);
        AddPersonal(ledger, resume, "phone", resume.Personal.Phone);
        AddPersonal(ledger, resume, "location", resume.Personal.Location);
        AddPersonal(ledger, resume, "linkedin", resume.Personal.LinkedInUrl);
        AddPersonal(ledger, resume, "github", resume.Personal.GitHubUrl);
        AddPersonal(ledger, resume, "website", resume.Personal.WebsiteUrl);
        AddPersonal(ledger, resume, "summary", resume.Personal.Summary);

        foreach (var experience in resume.Experience)
        {
            var subject = $"experience:{experience.Id:N}";
            var role = string.Join(" | ", new[]
                {
                    experience.Title, experience.Company, experience.Location,
                    experience.StartDate?.ToString("yyyy-MM-dd"),
                    experience.IsCurrent ? "present" : experience.EndDate?.ToString("yyyy-MM-dd")
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var method = experience.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase)
                ? "llm" : "deterministic";
            Add(ledger, resume, $"{subject}:role", "experience", role, subject, [], method);
            for (var index = 0; index < experience.Achievements.Count; index++)
                Add(ledger, resume, $"{subject}:achievement:{index + 1}", "achievement",
                    experience.Achievements[index], subject, ConceptsIn(experience.Achievements[index], resume), method);
            for (var index = 0; index < experience.Technologies.Count; index++)
                Add(ledger, resume, $"{subject}:technology:{index + 1}", "technology",
                    experience.Technologies[index], subject, [experience.Technologies[index]], method);
        }

        foreach (var education in resume.Education)
        {
            var text = string.Join(" | ", new[] { education.Degree, education.FieldOfStudy, education.Institution }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            Add(ledger, resume, $"education:{education.Id:N}", "education", text, $"education:{education.Id:N}", []);
        }

        foreach (var project in resume.Projects)
        {
            var subject = $"project:{project.Id:N}";
            Add(ledger, resume, $"{subject}:description", "project", $"{project.Name}: {project.Description}".TrimEnd(' ', ':'),
                subject, project.Technologies, externalUri: project.Url);
        }

        foreach (var skill in resume.Skills)
            Add(ledger, resume, SkillLocator(skill.Name), "skill", skill.Name, null, [skill.Name],
                skill.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase) ? "llm" : "deterministic");

        foreach (var entity in resume.Entities.Where(entity => !string.IsNullOrWhiteSpace(entity.Value)))
            AddEntityEvidence(ledger, resume, entity);

        resume.EvidenceLedger = ledger;
        return ledger;
    }

    private static void AddPersonal(EvidenceLedger ledger, ResumeDocument resume, string field, string? value,
        bool llmExtracted = false)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Add(ledger, resume, $"personal:{field}", "personal", value, "personal", [], llmExtracted ? "llm" : "deterministic");
    }

    private static bool WasLlmExtracted(ResumeDocument resume, string? value, string classification) =>
        !string.IsNullOrWhiteSpace(value) && resume.Entities.Any(entity =>
            entity.Source == DetectionSource.Llm &&
            string.Equals(entity.Classification, classification, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entity.Value, value, StringComparison.OrdinalIgnoreCase));

    private static void Add(EvidenceLedger ledger, ResumeDocument resume, string locator, string kind, string text,
        string? subject, IEnumerable<string> concepts, string method = "deterministic", double confidence = 1.0,
        string? externalUri = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var id = $"evidence:{resume.ResumeId:N}:{locator}";
        ledger.Evidence.Add(new EvidenceRecord
        {
            Id = id,
            SourceResumeId = resume.ResumeId,
            SourceName = resume.FileName,
            Kind = kind,
            Locator = locator,
            Text = text.Trim(),
            FastHash = FastHash(text),
            ExtractionMethod = method,
            Confidence = confidence,
            ExternalUri = Uri.TryCreate(externalUri, UriKind.Absolute, out _) ? externalUri : null
        });
        ledger.Claims.Add(new LedgerClaim
        {
            Id = $"claim:{resume.ResumeId:N}:{locator}",
            Kind = kind,
            Statement = text.Trim(),
            SubjectId = subject,
            EvidenceIds = [id],
            Concepts = concepts.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Origin = method is "ner" or "llm" ? "extracted" : "ingested",
            Review = method == "deterministic" ? "accepted" : "required"
        });
    }

    private static void AddEntityEvidence(EvidenceLedger ledger, ResumeDocument resume, ExtractedEntity entity)
    {
        var locator = $"entity:{entity.EntityId:N}";
        if (ledger.Evidence.Any(record => string.Equals(record.Locator, locator, StringComparison.Ordinal))) return;
        Add(ledger, resume, locator, "entity", entity.Value, null,
            entity.Classification.Contains("Skill", StringComparison.OrdinalIgnoreCase) ? [entity.Value] : [],
            entity.Source == DetectionSource.Ner ? "ner" : "extracted", entity.Confidence);
    }

    private static IEnumerable<string> ConceptsIn(string text, ResumeDocument resume) => resume.Skills
        .Select(skill => skill.Name)
        .Concat(resume.Entities.Where(entity => entity.Classification.Contains("Skill", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.Value))
        .Where(skill => skill.Length >= 2 && text.Contains(skill, StringComparison.OrdinalIgnoreCase));

    private static string ComputeSourceRevision(ResumeDocument resume)
    {
        var parts = new List<string?>
        {
            resume.Personal.FullName, resume.Personal.Email, resume.Personal.Phone, resume.Personal.Location,
            resume.Personal.LinkedInUrl, resume.Personal.GitHubUrl, resume.Personal.WebsiteUrl, resume.Personal.Summary
        };
        foreach (var experience in resume.Experience)
        {
            parts.Add(experience.Id.ToString("N"));
            parts.Add($"{experience.Company}|{experience.Title}|{experience.Location}|{experience.StartDate}|{experience.EndDate}|{experience.IsCurrent}");
            parts.AddRange(experience.Achievements);
            parts.AddRange(experience.Technologies);
        }
        foreach (var education in resume.Education)
            parts.Add($"{education.Id:N}|{education.Degree}|{education.FieldOfStudy}|{education.Institution}|{education.StartDate}|{education.EndDate}");
        foreach (var project in resume.Projects)
            parts.Add($"{project.Id:N}|{project.Name}|{project.Description}|{project.Url}|{project.Date}|{string.Join('|', project.Technologies)}");
        parts.AddRange(resume.Skills.Select(skill => $"{skill.Name}|{skill.Category}|{skill.YearsExperience}"));
        parts.AddRange(resume.Entities.Select(entity => $"{entity.EntityId:N}|{entity.Value}|{entity.Classification}|{entity.Confidence}"));
        return FastHash(string.Join('\n', parts));
    }

    private static string Normalize(string value) => Whitespace().Replace(value.Trim(), " ");
    public static string SkillLocator(string value) => $"skill:{Slug(value)}-{FastHash(value)[^8..]}";

    public static string Slug(string value)
    {
        value = value.Replace("ASP.NET", "aspnet", StringComparison.OrdinalIgnoreCase)
            .Replace(".NET", "dotnet", StringComparison.OrdinalIgnoreCase)
            .Replace("C++", "cpp", StringComparison.OrdinalIgnoreCase)
            .Replace("C#", "csharp", StringComparison.OrdinalIgnoreCase);
        var slug = NonSlug().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
