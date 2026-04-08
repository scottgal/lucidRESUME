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

    /// <summary>
    /// Preview what a merge would change WITHOUT modifying the target.
    /// Returns an ImportPreview with accept/reject toggles on every item.
    /// </summary>
    public async Task<ImportPreview> PreviewMergeAsync(
        ResumeDocument target, ResumeDocument incoming, string sourceName, CancellationToken ct = default)
    {
        var preview = new ImportPreview { SourceName = sourceName, Incoming = incoming };

        // Personal info changes
        PreviewPersonalInfo(target.Personal, incoming.Personal, sourceName, preview);

        // Experience: classify as new or merge
        foreach (var exp in incoming.Experience)
        {
            var match = await FindMatchingExperienceAsync(target.Experience, exp, ct);
            if (match != null)
            {
                var titleDiffers = match.Title != null && exp.Title != null &&
                    !match.Title.Equals(exp.Title, StringComparison.OrdinalIgnoreCase);
                var datesDiffer = match.StartDate.HasValue && exp.StartDate.HasValue &&
                    Math.Abs(match.StartDate.Value.DayNumber - exp.StartDate.Value.DayNumber) > 90;
                var newAchievements = exp.Achievements.Count(a =>
                    !match.Achievements.Any(ma => ma.Contains(a, StringComparison.OrdinalIgnoreCase)
                                                   || a.Contains(ma, StringComparison.OrdinalIgnoreCase)));
                var newTechs = exp.Technologies.Count(t =>
                    !match.Technologies.Contains(t, StringComparer.OrdinalIgnoreCase));

                preview.MergedExperience.Add(new ExperienceMergePreview
                {
                    Existing = match,
                    Incoming = exp,
                    TitleDiffers = titleDiffers,
                    DatesDiffer = datesDiffer,
                    NewAchievementsCount = newAchievements,
                    NewTechnologiesCount = newTechs,
                });

                if (titleDiffers)
                    preview.Anomalies.Add(new ImportAnomaly
                    {
                        Type = AnomalyType.TitleMismatch,
                        Description = $"Title differs for {match.Company}: '{match.Title}' vs '{exp.Title}'",
                        Severity = AnomalySeverity.Info, Source = sourceName,
                    });
                if (datesDiffer)
                    preview.Anomalies.Add(new ImportAnomaly
                    {
                        Type = AnomalyType.DateMismatch,
                        Description = $"Start date differs for {match.Company}: {match.StartDate:MMM yyyy} vs {exp.StartDate:MMM yyyy}",
                        Severity = AnomalySeverity.Warning, Source = sourceName,
                    });
            }
            else
            {
                preview.NewExperience.Add(new ReviewableItem<WorkExperience> { Item = exp });
            }
        }

        // Skills
        foreach (var skill in incoming.Skills)
        {
            var existing = await FindMatchingSkillAsync(target.Skills, skill.Name, ct);
            if (existing != null)
            {
                preview.UpdatedSkills.Add(new SkillUpdatePreview
                {
                    Existing = existing,
                    Incoming = skill,
                    EndorsementChanged = skill.EndorsementCount > existing.EndorsementCount,
                    YearsChanged = (skill.YearsExperience ?? 0) > (existing.YearsExperience ?? 0),
                });
            }
            else
            {
                preview.NewSkills.Add(new ReviewableItem<Skill> { Item = skill });
            }
        }

        // Education
        foreach (var edu in incoming.Education)
        {
            var existing = await FindMatchingEducationAsync(target.Education, edu, ct);
            if (existing == null)
                preview.NewEducation.Add(new ReviewableItem<Education> { Item = edu });
        }

        // Projects
        foreach (var proj in incoming.Projects)
        {
            var existing = target.Projects.FirstOrDefault(p =>
                p.Name.Equals(proj.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                preview.NewProjects.Add(new ReviewableItem<Project> { Item = proj });
        }

        // Personal info name conflict
        if (target.Personal.FullName != null && incoming.Personal.FullName != null &&
            !target.Personal.FullName.Equals(incoming.Personal.FullName, StringComparison.OrdinalIgnoreCase))
        {
            preview.Anomalies.Add(new ImportAnomaly
            {
                Type = AnomalyType.NameMismatch,
                Description = $"Name differs: '{target.Personal.FullName}' vs '{incoming.Personal.FullName}'",
                Severity = AnomalySeverity.Warning, Source = sourceName,
            });
        }

        return preview;
    }

    /// <summary>
    /// Apply only the accepted items from a preview to the target document.
    /// </summary>
    public void ApplyPreview(ResumeDocument target, ImportPreview preview)
    {
        var source = preview.SourceName;

        // Personal info
        foreach (var change in preview.PersonalInfoChanges.Where(c => c.IsAccepted && c.IncomingValue != null))
        {
            switch (change.FieldName)
            {
                case "FullName": target.Personal.FullName = change.IncomingValue; break;
                case "Email": target.Personal.Email = change.IncomingValue; break;
                case "Phone": target.Personal.Phone = change.IncomingValue; break;
                case "Location": target.Personal.Location = change.IncomingValue; break;
                case "LinkedInUrl": target.Personal.LinkedInUrl = change.IncomingValue; break;
                case "GitHubUrl": target.Personal.GitHubUrl = change.IncomingValue; break;
                case "WebsiteUrl": target.Personal.WebsiteUrl = change.IncomingValue; break;
                case "Summary": target.Personal.Summary = change.IncomingValue; break;
            }
        }

        // New experience
        foreach (var item in preview.NewExperience.Where(i => i.IsAccepted))
        {
            item.Item.ImportSources.Add(source);
            target.Experience.Add(item.Item);
        }

        // Merged experience
        foreach (var merge in preview.MergedExperience.Where(m => m.IsAccepted))
            MergeExperience(merge.Existing, merge.Incoming, source);

        // New skills
        foreach (var item in preview.NewSkills.Where(i => i.IsAccepted))
        {
            item.Item.ImportSources.Add(source);
            target.Skills.Add(item.Item);
        }

        // Updated skills
        foreach (var update in preview.UpdatedSkills.Where(u => u.IsAccepted))
        {
            if (!update.Existing.ImportSources.Contains(source))
                update.Existing.ImportSources.Add(source);
            update.Existing.Category ??= update.Incoming.Category;
            if (update.Incoming.YearsExperience.HasValue)
                update.Existing.YearsExperience = Math.Max(update.Existing.YearsExperience ?? 0, update.Incoming.YearsExperience.Value);
            update.Existing.EndorsementCount = Math.Max(update.Existing.EndorsementCount, update.Incoming.EndorsementCount);
        }

        // Education
        foreach (var item in preview.NewEducation.Where(i => i.IsAccepted))
        {
            item.Item.ImportSources.Add(source);
            target.Education.Add(item.Item);
        }

        // Projects
        foreach (var item in preview.NewProjects.Where(i => i.IsAccepted))
        {
            item.Item.ImportSources.Add(source);
            target.Projects.Add(item.Item);
        }

        target.LastModifiedAt = DateTimeOffset.UtcNow;
    }

    private static void PreviewPersonalInfo(PersonalInfo current, PersonalInfo incoming, string source, ImportPreview preview)
    {
        void Check(string field, string? cur, string? inc)
        {
            if (inc == null) return;
            if (cur == null)
                preview.PersonalInfoChanges.Add(new FieldChange { FieldName = field, CurrentValue = cur, IncomingValue = inc });
            else if (!cur.Equals(inc, StringComparison.OrdinalIgnoreCase))
                preview.PersonalInfoChanges.Add(new FieldChange { FieldName = field, CurrentValue = cur, IncomingValue = inc, IsConflict = true });
        }

        Check("FullName", current.FullName, incoming.FullName);
        Check("Email", current.Email, incoming.Email);
        Check("Phone", current.Phone, incoming.Phone);
        Check("Location", current.Location, incoming.Location);
        Check("LinkedInUrl", current.LinkedInUrl, incoming.LinkedInUrl);
        Check("GitHubUrl", current.GitHubUrl, incoming.GitHubUrl);
        Check("WebsiteUrl", current.WebsiteUrl, incoming.WebsiteUrl);
        Check("Summary", current.Summary, incoming.Summary);
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
