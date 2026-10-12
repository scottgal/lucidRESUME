using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Models.Evidence;

/// <summary>Creates and refreshes the evidence ledger from ingestion results.</summary>
public static partial class EvidenceLedgerBuilder
{
    private static readonly ConcurrentDictionary<string, Regex> SkillPatterns = new(StringComparer.OrdinalIgnoreCase);
    public static EvidenceLedger EnsureCurrent(ResumeDocument resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        var revision = ComputeSourceRevision(resume);
        if (resume.EvidenceLedger.SourceRevision == revision && resume.EvidenceLedger.Evidence.Count > 0)
            return resume.EvidenceLedger;
        return Rebuild(resume, revision);
    }

    public static EvidenceLedger Rebuild(ResumeDocument resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        return Rebuild(resume, ComputeSourceRevision(resume));
    }

    /// <summary>
    /// Rebuilds the merged document's claims while retaining the original evidence records
    /// from every imported document. Equivalent source passages become multiple supports for
    /// one merged claim instead of being flattened into an anonymous aggregate.
    /// </summary>
    public static EvidenceLedger RebuildFromSources(ResumeDocument resume, IEnumerable<EvidenceLedger> sources)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(sources);
        var sourceLedgers = sources.ToList();
        var sourceEvidence = sourceLedgers.SelectMany(source => source.Evidence)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var claimsByEvidence = sourceLedgers.SelectMany(source => source.Claims)
            .SelectMany(claim => claim.EvidenceIds.Select(id => (id, claim)))
            .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.claim).ToList(), StringComparer.OrdinalIgnoreCase);
        var acceptedSupports = sourceLedgers.SelectMany(source => source.Claims)
            .Where(original => original.Review == "accepted").SelectMany(original => original.EvidenceIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ledger = Rebuild(resume);
        foreach (var evidence in ledger.Evidence)
            evidence.Title = "Canonical career ledger";
        foreach (var claim in ledger.Claims)
        {
            var aggregateEvidence = claim.EvidenceIds
                .Select(id => ledger.Evidence.First(item => item.Id == id))
                .First();
            var originals = sourceEvidence.Where(item =>
                    string.Equals(item.Kind, aggregateEvidence.Kind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Normalize(item.Text), Normalize(aggregateEvidence.Text), StringComparison.OrdinalIgnoreCase) &&
                    (claim.SubjectId?.StartsWith("experience:") != true ||
                     claimsByEvidence.TryGetValue(item.Id, out var originalClaims) && originalClaims.Any(original =>
                         RoleSubjectMatches(original.SubjectId, claim.SubjectId, resume.AggregateRoleIds))))
                .ToList();
            if (originals.Count == 0) continue;
            // Rejected duplicate passages must not become supports for an accepted copy.
            if (originals.Any(item => acceptedSupports.Contains(item.Id)))
                originals = originals.Where(item => acceptedSupports.Contains(item.Id)).ToList();
            claim.EvidenceIds = originals.Select(item => item.Id).ToList();
            var reviews = sourceLedgers.SelectMany(source => source.Claims)
                .Where(original => original.EvidenceIds.Intersect(claim.EvidenceIds, StringComparer.OrdinalIgnoreCase).Any())
                .Select(original => original.Review).ToList();
            if (claim.Review != "rejected" && reviews.Count > 0)
                claim.Review = reviews.Any(review => review == "accepted") ? "accepted"
                    : reviews.All(review => review == "rejected") ? "rejected" : "required";
            if (originals.All(item => item.ExtractionMethod is "ner" or "llm" or "extracted"))
            {
                claim.Origin = "derived";
            }
            ledger.Evidence.Remove(aggregateEvidence);
            foreach (var original in originals)
                if (ledger.Evidence.All(item => !string.Equals(item.Id, original.Id, StringComparison.OrdinalIgnoreCase)))
                    ledger.Evidence.Add(original);
        }
        resume.EvidenceLedger = ledger;
        return ledger;
    }

    private static bool RoleSubjectMatches(string? source, string target, IReadOnlyDictionary<Guid, Guid> roleIds)
    {
        if (source == target) return true;
        return source?.StartsWith("experience:") == true &&
            Guid.TryParse(source["experience:".Length..], out var original) &&
            Guid.TryParse(target["experience:".Length..], out var merged) &&
            roleIds.TryGetValue(original, out var mapped) && mapped == merged;
    }

    public static string FastHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
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
        var previous = resume.EvidenceLedger;
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
                    experience.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    experience.IsCurrent ? "present" : experience.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var method = experience.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase)
                ? "llm" : "deterministic";
            Add(ledger, resume, $"{subject}:role", "experience", role, subject, [], method);
            for (var index = 0; index < experience.Achievements.Count; index++)
            {
                var interview = experience.InterviewEntries.FirstOrDefault(entry => entry.ReviewedProse == experience.Achievements[index]);
                Add(ledger, resume, $"{subject}:achievement:{index + 1}", "achievement",
                    experience.Achievements[index], subject, ConceptsIn(experience.Achievements[index], resume),
                    interview is not null ? "author-interview" : method);
                if (interview is not null)
                {
                    var evidence = ledger.Evidence[^1];
                    evidence.SourceResumeId = interview.Id;
                    evidence.SourceName = $"Role interview: {experience.Title} at {experience.Company}";
                    evidence.Title = "Author-reviewed interview account; candidate declaration";
                    evidence.AccessedOn = DateOnly.FromDateTime(interview.AcceptedAt.UtcDateTime);
                }
            }
            for (var index = 0; index < experience.Technologies.Count; index++)
            {
                var interview = experience.InterviewEntries.FirstOrDefault(entry =>
                    entry.Skills.Contains(experience.Technologies[index], StringComparer.OrdinalIgnoreCase));
                Add(ledger, resume, $"{subject}:technology:{index + 1}", "technology",
                    experience.Technologies[index], subject, [experience.Technologies[index]],
                    interview is null ? method : "author-interview");
                if (interview is not null)
                {
                    var evidence = ledger.Evidence[^1];
                    evidence.SourceResumeId = interview.Id;
                    evidence.SourceName = $"Role interview: {experience.Title} at {experience.Company}";
                    evidence.Title = "Author-reviewed skill declaration";
                    evidence.AccessedOn = DateOnly.FromDateTime(interview.AcceptedAt.UtcDateTime);
                }
            }
        }

        foreach (var education in resume.Education)
        {
            var text = string.Join(" | ", new[] { education.Degree, education.FieldOfStudy, education.Institution }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            Add(ledger, resume, $"education:{education.Id:N}", "education", text, $"education:{education.Id:N}", []);
        }

        foreach (var project in resume.Projects)
        {
            if (project.EvidenceMetadata.TryGetValue("eligible_for_personal_evidence", out var eligible) &&
                bool.TryParse(eligible, out var mayUse) && !mayUse)
                continue;
            var subject = $"project:{project.Id:N}";
            Add(ledger, resume, $"{subject}:description", "project", $"{project.Name}: {project.Description}".TrimEnd(' ', ':'),
                subject, DeclaredProjectSkills(project),
                externalUri: project.Url);
            if (project.EvidenceMetadata.GetValueOrDefault("provider") == "github")
                foreach (var technology in project.Technologies)
                    Add(ledger, resume, $"{subject}:technology:{Slug(technology)}", "technology", technology,
                        subject, [technology], method: "extracted", externalUri: project.Url, confidence: 0.7);
            for (var index = 0; index < project.EvidencePassages.Count; index++)
            {
                var passage = project.EvidencePassages[index];
                Add(ledger, resume, $"{subject}:readme:{index + 1}", "project", $"{project.Name}: {passage.Text}",
                    subject, passage.Skills, method: "extracted", externalUri: passage.SourceUri, confidence: 0.8);
                ledger.Evidence[^1].ExtractionMethod = "repository-readme";
                ledger.Evidence[^1].Title = $"{project.Name} README ({passage.Revision ?? "observed revision"})";
                ledger.Claims[^1].Origin = "derived";
            }
        }

        // Publication links support the author's account; they do not declare skills or role experience.
        foreach (var publication in resume.Publications)
            Add(ledger, resume, $"publication:{publication.Id:N}", "publication",
                $"{publication.Name}: {publication.Description}".TrimEnd(' ', ':'),
                $"publication:{publication.Id:N}", [], externalUri: publication.Url);

        foreach (var skill in resume.Skills)
            Add(ledger, resume, SkillLocator(skill.Name), "skill", skill.Name, null, [skill.Name],
                skill.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase) ? "llm" : "deterministic");

        foreach (var entity in resume.Entities.Where(entity => !string.IsNullOrWhiteSpace(entity.Value)))
            AddEntityEvidence(ledger, resume, entity);

        // An appended interview must not reset review decisions or original import provenance.
        foreach (var claim in ledger.Claims)
        {
            var old = previous.Claims.FirstOrDefault(c => c.Id == claim.Id && c.Statement == claim.Statement);
            if (old is null) continue;
            var supports = previous.Evidence.Where(e => old.EvidenceIds.Contains(e.Id)).ToList();
            if (supports.Count != old.EvidenceIds.Count) continue;
            ledger.Evidence.RemoveAll(e => claim.EvidenceIds.Contains(e.Id));
            foreach (var support in supports)
                if (ledger.Evidence.All(e => e.Id != support.Id)) ledger.Evidence.Add(support);
            claim.EvidenceIds = [.. old.EvidenceIds];
            claim.Review = old.Review;
            claim.Origin = old.Origin;
        }
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
            Origin = method is "ner" or "llm" or "extracted" ? "derived" : "declared",
            Review = method is "deterministic" or "author-interview" ? "accepted" : "required"
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

    private static IEnumerable<string> DeclaredProjectSkills(Project project)
    {
        if (project.EvidenceMetadata.GetValueOrDefault("provider") != "github") return project.Technologies;
        var preserved = project.EvidenceMetadata.GetValueOrDefault("human_declared_technologies");
        if (preserved is null) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(preserved) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static IEnumerable<string> ConceptsIn(string text, ResumeDocument resume) => resume.Skills
        .Select(skill => skill.Name)
        .Concat(resume.Experience.SelectMany(role => role.Technologies))
        .Concat(resume.Entities.Where(entity => entity.Classification.Contains("Skill", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.Value))
        .Where(skill => skill.Length >= 1 && SkillPattern(skill).IsMatch(text));

    private static Regex SkillPattern(string skill)
    {
        if (SkillPatterns.TryGetValue(skill, out var pattern)) return pattern;
        if (SkillPatterns.Count > 4096) SkillPatterns.Clear();
        return SkillPatterns.GetOrAdd(skill, value => new Regex(
            @"(?<![\p{L}\p{N}+#])" + Regex.Escape(value) + @"(?![\p{L}\p{N}+#])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static string ComputeSourceRevision(ResumeDocument resume)
    {
        var parts = new List<string?>
        {
            resume.Personal.FullName, resume.Personal.Email, resume.Personal.Phone, resume.Personal.ContactPreference,
            resume.Personal.Location,
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
            parts.Add($"{project.Id:N}|{project.Name}|{project.Description}|{project.Url}|{project.Date}|{string.Join('|', project.Technologies)}|{string.Join('|', project.EvidenceMetadata.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"))}");
        parts.AddRange(resume.Projects.SelectMany(project => project.EvidencePassages)
            .Select(passage => $"{passage.Text}|{passage.SourceUri}|{passage.Revision}|{string.Join('|', passage.Skills)}"));
        parts.AddRange(resume.Publications.Select(item => $"{item.Id:N}|{item.Name}|{item.Description}|{item.Url}|{item.Date}"));
        parts.AddRange(resume.Skills.Select(skill => $"{skill.Name}|{skill.Category}|{skill.YearsExperience}"));
        parts.AddRange(resume.Entities.Select(entity => $"{entity.EntityId:N}|{entity.Value}|{entity.Classification}|{entity.Confidence}"));
        return FastHash(string.Join('\n', parts));
    }

    private static string Normalize(string value) => Whitespace().Replace(value.Trim(), " ");
    public static string SkillLocator(string value) => $"skill:{Slug(value)}-{FastHash(value)[^8..]}";

    public static Guid StableGuid(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string Slug(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
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
