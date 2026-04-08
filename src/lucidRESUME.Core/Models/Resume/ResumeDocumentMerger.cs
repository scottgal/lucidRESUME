using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Merges data from a new import into an existing ResumeDocument.
/// Uses embedding cosine similarity for semantic matching — no brittle string hacks.
/// </summary>
public sealed class ResumeDocumentMerger
{
    private readonly IEmbeddingService _embedder;
    private const float CompanyMatchThreshold = 0.75f;
    private const float TitleMatchThreshold = 0.70f;
    private const int DateOverlapGraceDays = 90;

    public ResumeDocumentMerger(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    public async Task<List<ImportAnomaly>> MergeIntoAsync(
        ResumeDocument target, ResumeDocument incoming, string sourceName, CancellationToken ct = default)
    {
        var anomalies = new List<ImportAnomaly>();

        MergePersonalInfo(target.Personal, incoming.Personal, sourceName, anomalies);

        // Experience: semantic match by company + date overlap
        foreach (var exp in incoming.Experience)
        {
            exp.ImportSources.Add(sourceName);
            var match = await FindMatchingExperienceAsync(target.Experience, exp, ct);
            if (match != null)
            {
                await DetectExperienceAnomaliesAsync(match, exp, sourceName, anomalies, ct);
                MergeExperience(match, exp, sourceName);
            }
            else
            {
                target.Experience.Add(exp);
            }
        }

        // Skills: semantic match by name
        foreach (var skill in incoming.Skills)
        {
            skill.ImportSources.Add(sourceName);
            var existing = await FindMatchingSkillAsync(target.Skills, skill.Name, ct);
            if (existing != null)
            {
                if (!existing.ImportSources.Contains(sourceName))
                    existing.ImportSources.Add(sourceName);
                existing.Category ??= skill.Category;
                if (skill.YearsExperience.HasValue)
                    existing.YearsExperience = Math.Max(existing.YearsExperience ?? 0, skill.YearsExperience.Value);
                existing.EndorsementCount = Math.Max(existing.EndorsementCount, skill.EndorsementCount);
            }
            else
            {
                target.Skills.Add(skill);
            }
        }

        // Education: semantic match by institution
        foreach (var edu in incoming.Education)
        {
            edu.ImportSources.Add(sourceName);
            var existing = await FindMatchingEducationAsync(target.Education, edu, ct);
            if (existing != null)
            {
                if (!existing.ImportSources.Contains(sourceName))
                    existing.ImportSources.Add(sourceName);
                existing.Degree ??= edu.Degree;
                existing.FieldOfStudy ??= edu.FieldOfStudy;
                existing.StartDate ??= edu.StartDate;
                existing.EndDate ??= edu.EndDate;
            }
            else
            {
                target.Education.Add(edu);
            }
        }

        // Projects: match by name (case-insensitive — project names are specific enough)
        foreach (var proj in incoming.Projects)
        {
            proj.ImportSources.Add(sourceName);
            var existing = target.Projects.FirstOrDefault(p =>
                p.Name.Equals(proj.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!existing.ImportSources.Contains(sourceName))
                    existing.ImportSources.Add(sourceName);
                existing.Description ??= proj.Description;
                existing.Url ??= proj.Url;
                foreach (var tech in proj.Technologies)
                    if (!existing.Technologies.Contains(tech, StringComparer.OrdinalIgnoreCase))
                        existing.Technologies.Add(tech);
            }
            else
            {
                target.Projects.Add(proj);
            }
        }

        foreach (var cert in incoming.Certifications)
        {
            if (!target.Certifications.Any(c => c.Name.Equals(cert.Name, StringComparison.OrdinalIgnoreCase)))
                target.Certifications.Add(cert);
        }

        target.Entities.AddRange(incoming.Entities);

        if (!string.IsNullOrWhiteSpace(incoming.PlainText))
            target.PlainText = string.Join("\n\n",
                new[] { target.PlainText, incoming.PlainText }.Where(s => !string.IsNullOrWhiteSpace(s)));

        target.LastModifiedAt = DateTimeOffset.UtcNow;
        return anomalies;
    }

    private async Task<WorkExperience?> FindMatchingExperienceAsync(
        List<WorkExperience> existing, WorkExperience incoming, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(incoming.Company)) return null;

        var incomingEmb = await _embedder.EmbedAsync(incoming.Company, ct);

        foreach (var exp in existing)
        {
            if (string.IsNullOrWhiteSpace(exp.Company)) continue;

            var existingEmb = await _embedder.EmbedAsync(exp.Company, ct);
            var similarity = _embedder.CosineSimilarity(incomingEmb, existingEmb);

            if (similarity < CompanyMatchThreshold) continue;

            // Company matches — check date overlap
            if (exp.StartDate is null || incoming.StartDate is null) return exp;
            var aEnd = (exp.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : exp.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
            var bEnd = (incoming.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : incoming.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
            if (exp.StartDate.Value.DayNumber <= bEnd + DateOverlapGraceDays &&
                incoming.StartDate.Value.DayNumber <= aEnd + DateOverlapGraceDays)
                return exp;
        }
        return null;
    }

    private async Task<Skill?> FindMatchingSkillAsync(List<Skill> existing, string skillName, CancellationToken ct)
    {
        // Fast exact match first
        var exact = existing.FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Semantic match for aliases ("K8s" ≈ "Kubernetes")
        var incomingEmb = await _embedder.EmbedAsync(skillName, ct);
        foreach (var skill in existing)
        {
            var existingEmb = await _embedder.EmbedAsync(skill.Name, ct);
            if (_embedder.CosineSimilarity(incomingEmb, existingEmb) >= 0.85f)
                return skill;
        }
        return null;
    }

    private async Task<Education?> FindMatchingEducationAsync(
        List<Education> existing, Education incoming, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(incoming.Institution)) return null;

        var incomingEmb = await _embedder.EmbedAsync(incoming.Institution, ct);
        foreach (var edu in existing)
        {
            if (string.IsNullOrWhiteSpace(edu.Institution)) continue;
            var existingEmb = await _embedder.EmbedAsync(edu.Institution, ct);
            if (_embedder.CosineSimilarity(incomingEmb, existingEmb) >= CompanyMatchThreshold)
                return edu;
        }
        return null;
    }

    private async Task DetectExperienceAnomaliesAsync(
        WorkExperience existing, WorkExperience incoming, string source,
        List<ImportAnomaly> anomalies, CancellationToken ct)
    {
        // Title mismatch — use semantic similarity
        if (existing.Title != null && incoming.Title != null)
        {
            var simTitle = _embedder.CosineSimilarity(
                await _embedder.EmbedAsync(existing.Title, ct),
                await _embedder.EmbedAsync(incoming.Title, ct));

            if (simTitle < TitleMatchThreshold)
            {
                anomalies.Add(new ImportAnomaly
                {
                    Type = AnomalyType.TitleMismatch,
                    Description = $"Title differs for {existing.Company}: '{existing.Title}' vs '{incoming.Title}' (similarity {simTitle:P0}, from {source})",
                    Severity = AnomalySeverity.Info,
                    Source = source,
                });
            }
        }

        // Date mismatch (>3 months difference)
        if (existing.StartDate.HasValue && incoming.StartDate.HasValue)
        {
            var daysDiff = Math.Abs(existing.StartDate.Value.DayNumber - incoming.StartDate.Value.DayNumber);
            if (daysDiff > 90)
            {
                anomalies.Add(new ImportAnomaly
                {
                    Type = AnomalyType.DateMismatch,
                    Description = $"Start date differs by {daysDiff / 30} months for {existing.Company}: {existing.StartDate:MMM yyyy} vs {incoming.StartDate:MMM yyyy} (from {source})",
                    Severity = AnomalySeverity.Warning,
                    Source = source,
                });
            }
        }
    }

    private static void MergePersonalInfo(PersonalInfo target, PersonalInfo incoming, string source, List<ImportAnomaly> anomalies)
    {
        if (target.FullName != null && incoming.FullName != null &&
            !target.FullName.Equals(incoming.FullName, StringComparison.OrdinalIgnoreCase))
        {
            anomalies.Add(new ImportAnomaly
            {
                Type = AnomalyType.NameMismatch,
                Description = $"Name differs: '{target.FullName}' vs '{incoming.FullName}' (from {source})",
                Severity = AnomalySeverity.Warning,
                Source = source,
            });
        }

        target.FullName ??= incoming.FullName;
        target.Email ??= incoming.Email;
        target.Phone ??= incoming.Phone;
        target.Location ??= incoming.Location;
        target.LinkedInUrl ??= incoming.LinkedInUrl;
        target.GitHubUrl ??= incoming.GitHubUrl;
        target.WebsiteUrl ??= incoming.WebsiteUrl;
        target.Summary ??= incoming.Summary;
    }

    private static void MergeExperience(WorkExperience target, WorkExperience incoming, string source)
    {
        if (!target.ImportSources.Contains(source))
            target.ImportSources.Add(source);

        if ((incoming.Title?.Length ?? 0) > (target.Title?.Length ?? 0))
            target.Title = incoming.Title;

        target.Location ??= incoming.Location;

        if (incoming.StartDate.HasValue && (target.StartDate is null || incoming.StartDate < target.StartDate))
            target.StartDate = incoming.StartDate;
        if (incoming.IsCurrent) target.IsCurrent = true;
        if (!target.IsCurrent && incoming.EndDate.HasValue && (target.EndDate is null || incoming.EndDate > target.EndDate))
            target.EndDate = incoming.EndDate;

        foreach (var tech in incoming.Technologies)
            if (!target.Technologies.Contains(tech, StringComparer.OrdinalIgnoreCase))
                target.Technologies.Add(tech);

        foreach (var ach in incoming.Achievements)
            if (!target.Achievements.Any(a => a.Contains(ach, StringComparison.OrdinalIgnoreCase)
                                               || ach.Contains(a, StringComparison.OrdinalIgnoreCase)))
                target.Achievements.Add(ach);
    }
}

public sealed class ImportAnomaly
{
    public AnomalyType Type { get; init; }
    public string Description { get; init; } = "";
    public AnomalySeverity Severity { get; init; }
    public string Source { get; init; } = "";
}

public enum AnomalyType
{
    NameMismatch,
    TitleMismatch,
    DateMismatch,
    SkillMismatch,
    LocationMismatch,
}

public enum AnomalySeverity
{
    Info,
    Warning,
    Error,
}
