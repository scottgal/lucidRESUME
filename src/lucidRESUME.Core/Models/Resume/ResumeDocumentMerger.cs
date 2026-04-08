namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Merges data from a new import into an existing ResumeDocument.
/// All imports (DOCX, LinkedIn, GitHub, manual) feed the SAME document.
/// Source tracking on each element for provenance and anomaly detection.
/// </summary>
public static class ResumeDocumentMerger
{
    /// <summary>
    /// Merge an incoming document into the target, deduplicating and source-tracking.
    /// Returns any cross-source anomalies detected.
    /// </summary>
    public static List<ImportAnomaly> MergeInto(ResumeDocument target, ResumeDocument incoming, string sourceName)
    {
        var anomalies = new List<ImportAnomaly>();

        // Personal info: fill gaps, detect conflicts
        MergePersonalInfo(target.Personal, incoming.Personal, sourceName, anomalies);

        // Experience: match by company+date overlap, merge or add
        foreach (var exp in incoming.Experience)
        {
            exp.ImportSources.Add(sourceName);
            var match = FindMatchingExperience(target.Experience, exp);
            if (match != null)
            {
                // Detect anomalies before merging
                DetectExperienceAnomalies(match, exp, sourceName, anomalies);
                MergeExperience(match, exp, sourceName);
            }
            else
            {
                target.Experience.Add(exp);
            }
        }

        // Skills: merge by name, track sources
        foreach (var skill in incoming.Skills)
        {
            skill.ImportSources.Add(sourceName);
            var existing = target.Skills.FirstOrDefault(s =>
                s.Name.Equals(skill.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!existing.ImportSources.Contains(sourceName))
                    existing.ImportSources.Add(sourceName);
                existing.Category ??= skill.Category;
                if (skill.YearsExperience.HasValue)
                    existing.YearsExperience = Math.Max(existing.YearsExperience ?? 0, skill.YearsExperience.Value);
            }
            else
            {
                target.Skills.Add(skill);
            }
        }

        // Education: match by institution
        foreach (var edu in incoming.Education)
        {
            edu.ImportSources.Add(sourceName);
            var existing = target.Education.FirstOrDefault(e =>
                NormalizeCompany(e.Institution ?? "").Equals(NormalizeCompany(edu.Institution ?? ""), StringComparison.OrdinalIgnoreCase));
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

        // Projects: match by name
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

        // Certifications
        foreach (var cert in incoming.Certifications)
        {
            if (!target.Certifications.Any(c => c.Name.Equals(cert.Name, StringComparison.OrdinalIgnoreCase)))
                target.Certifications.Add(cert);
        }

        // Entities
        target.Entities.AddRange(incoming.Entities);

        // Append raw text
        if (!string.IsNullOrWhiteSpace(incoming.PlainText))
            target.PlainText = string.Join("\n\n", new[] { target.PlainText, incoming.PlainText }.Where(s => !string.IsNullOrWhiteSpace(s)));

        target.LastModifiedAt = DateTimeOffset.UtcNow;

        return anomalies;
    }

    private static void MergePersonalInfo(PersonalInfo target, PersonalInfo incoming, string source, List<ImportAnomaly> anomalies)
    {
        // Detect conflicts
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

        // Fill gaps (don't overwrite existing)
        target.FullName ??= incoming.FullName;
        target.Email ??= incoming.Email;
        target.Phone ??= incoming.Phone;
        target.Location ??= incoming.Location;
        target.LinkedInUrl ??= incoming.LinkedInUrl;
        target.GitHubUrl ??= incoming.GitHubUrl;
        target.WebsiteUrl ??= incoming.WebsiteUrl;
        target.Summary ??= incoming.Summary;
    }

    private static WorkExperience? FindMatchingExperience(List<WorkExperience> existing, WorkExperience incoming)
    {
        foreach (var exp in existing)
        {
            var ca = NormalizeCompany(exp.Company ?? "");
            var cb = NormalizeCompany(incoming.Company ?? "");
            if (ca.Length == 0 || cb.Length == 0) continue;

            var companySimilar = ca.Contains(cb, StringComparison.OrdinalIgnoreCase)
                                 || cb.Contains(ca, StringComparison.OrdinalIgnoreCase);
            if (!companySimilar) continue;

            // Date overlap check (90-day grace)
            if (exp.StartDate is null || incoming.StartDate is null) return exp; // same company, trust it
            var aEnd = (exp.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : exp.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
            var bEnd = (incoming.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : incoming.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
            if (exp.StartDate.Value.DayNumber <= bEnd + 90 && incoming.StartDate.Value.DayNumber <= aEnd + 90)
                return exp;
        }
        return null;
    }

    private static void DetectExperienceAnomalies(WorkExperience existing, WorkExperience incoming, string source, List<ImportAnomaly> anomalies)
    {
        // Title mismatch
        if (existing.Title != null && incoming.Title != null &&
            !existing.Title.Equals(incoming.Title, StringComparison.OrdinalIgnoreCase))
        {
            anomalies.Add(new ImportAnomaly
            {
                Type = AnomalyType.TitleMismatch,
                Description = $"Title differs for {existing.Company}: '{existing.Title}' vs '{incoming.Title}' (from {source})",
                Severity = AnomalySeverity.Info,
                Source = source,
            });
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

    private static void MergeExperience(WorkExperience target, WorkExperience incoming, string source)
    {
        if (!target.ImportSources.Contains(source))
            target.ImportSources.Add(source);

        // Keep longer title
        if ((incoming.Title?.Length ?? 0) > (target.Title?.Length ?? 0))
            target.Title = incoming.Title;

        target.Location ??= incoming.Location;

        // Extend date range
        if (incoming.StartDate.HasValue && (target.StartDate is null || incoming.StartDate < target.StartDate))
            target.StartDate = incoming.StartDate;
        if (incoming.IsCurrent) target.IsCurrent = true;
        if (!target.IsCurrent && incoming.EndDate.HasValue && (target.EndDate is null || incoming.EndDate > target.EndDate))
            target.EndDate = incoming.EndDate;

        // Merge technologies
        foreach (var tech in incoming.Technologies)
            if (!target.Technologies.Contains(tech, StringComparer.OrdinalIgnoreCase))
                target.Technologies.Add(tech);

        // Merge unique achievements
        foreach (var ach in incoming.Achievements)
            if (!target.Achievements.Any(a => a.Contains(ach, StringComparison.OrdinalIgnoreCase)
                                               || ach.Contains(a, StringComparison.OrdinalIgnoreCase)))
                target.Achievements.Add(ach);
    }

    private static string NormalizeCompany(string name)
    {
        var suffixes = new[] { " ltd", " limited", " inc", " corp", " plc", " gmbh", " ab", " llc", " pty" };
        var lower = name.ToLowerInvariant().Trim().TrimEnd('.');
        foreach (var suffix in suffixes)
            if (lower.EndsWith(suffix))
                lower = lower[..^suffix.Length].TrimEnd(',', ' ');
        return lower;
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
