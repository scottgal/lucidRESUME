using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Builds a SkillLedger from a ResumeDocument by scanning all sections
/// and cross-referencing skills with experience date ranges.
/// Uses embeddings for semantic skill matching (e.g., "K8s" ≈ "Kubernetes").
/// </summary>
public sealed class SkillLedgerBuilder
{
    private readonly IEmbeddingService _embedder;

    public SkillLedgerBuilder(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    public async Task<SkillLedger> BuildAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var ledger = new SkillLedger();
        var entries = new Dictionary<string, SkillLedgerEntry>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect all skills from the Skills section
        foreach (var skill in resume.Skills)
        {
            var entry = GetOrCreate(entries, skill.Name);
            entry.Category ??= skill.Category;
            entry.Evidence.Add(new SkillEvidence
            {
                SourceText = skill.Name,
                Source = EvidenceSource.SkillsSection,
                Confidence = 0.9
            });
        }

        // 2. Scan experience entries for skill mentions
        foreach (var exp in resume.Experience)
        {
            // Technologies field — direct skill listing
            foreach (var tech in exp.Technologies)
            {
                var entry = GetOrCreate(entries, tech);
                entry.Evidence.Add(new SkillEvidence
                {
                    ExperienceId = exp.Id,
                    Company = exp.Company,
                    JobTitle = exp.Title,
                    SourceText = tech,
                    StartDate = exp.StartDate,
                    EndDate = exp.IsCurrent ? null : exp.EndDate,
                    Source = EvidenceSource.JobTechnology,
                    Confidence = 0.95
                });
            }

            // Achievement bullets — check each skill against each bullet
            foreach (var achievement in exp.Achievements)
            {
                var matchedSkills = FindSkillMentions(achievement, entries.Keys.ToList());
                foreach (var skillName in matchedSkills)
                {
                    var entry = GetOrCreate(entries, skillName);
                    entry.Evidence.Add(new SkillEvidence
                    {
                        ExperienceId = exp.Id,
                        Company = exp.Company,
                        JobTitle = exp.Title,
                        SourceText = achievement,
                        StartDate = exp.StartDate,
                        EndDate = exp.IsCurrent ? null : exp.EndDate,
                        Source = EvidenceSource.AchievementBullet,
                        Confidence = 0.7
                    });
                }
            }
        }

        // 3. Scan summary for skill mentions
        if (!string.IsNullOrEmpty(resume.Personal.Summary))
        {
            var summarySkills = FindSkillMentions(resume.Personal.Summary, entries.Keys.ToList());
            foreach (var skillName in summarySkills)
            {
                var entry = GetOrCreate(entries, skillName);
                entry.Evidence.Add(new SkillEvidence
                {
                    SourceText = resume.Personal.Summary,
                    Source = EvidenceSource.Summary,
                    Confidence = 0.6
                });
            }
        }

        // 4. NER-extracted skills
        foreach (var entity in resume.Entities.Where(e => e.Classification is "NerSkill"))
        {
            var entry = GetOrCreate(entries, entity.Value);
            entry.Evidence.Add(new SkillEvidence
            {
                SourceText = entity.Value,
                Source = EvidenceSource.NerExtracted,
                Confidence = entity.Confidence
            });
        }

        // 5. Calculate years and dates for each skill
        foreach (var entry in entries.Values)
        {
            CalculateYears(entry);
        }

        // 6. Find consistency issues
        var issues = FindConsistencyIssues(entries.Values.ToList(), resume);

        ledger.Entries = entries.Values
            .OrderByDescending(e => e.Strength)
            .ToList();
        ledger.Issues = issues;

        return ledger;
    }

    private static Guid? ParseGuidSafe(string? s) =>
        s is not null && Guid.TryParse(s.AsSpan(), out var g) ? g : null;

    private static SkillLedgerEntry GetOrCreate(Dictionary<string, SkillLedgerEntry> entries, string skillName)
    {
        var normalized = skillName.Trim();
        if (!entries.TryGetValue(normalized, out var entry))
        {
            entry = new SkillLedgerEntry { SkillName = normalized };
            entries[normalized] = entry;
        }
        return entry;
    }

    /// <summary>Simple substring matching for skill mentions in text. Fast, not semantic.</summary>
    private static List<string> FindSkillMentions(string text, List<string> knownSkills)
    {
        var lower = text.ToLowerInvariant();
        return knownSkills
            .Where(s => s.Length >= 2 && lower.Contains(s.ToLowerInvariant()))
            .ToList();
    }

    private static void CalculateYears(SkillLedgerEntry entry)
    {
        var datedEvidence = entry.Evidence
            .Where(e => e.StartDate.HasValue)
            .OrderBy(e => e.StartDate)
            .ToList();

        if (datedEvidence.Count == 0)
        {
            entry.CalculatedYears = 0;
            return;
        }

        entry.FirstSeen = datedEvidence.First().StartDate;
        entry.LastSeen = datedEvidence.Last().EndDate;
        entry.IsCurrent = datedEvidence.Any(e => e.EndDate is null);

        // Merge overlapping date ranges to avoid double-counting
        var ranges = datedEvidence
            .Select(e => (
                start: e.StartDate!.Value.DayNumber,
                end: (e.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber))
            .OrderBy(r => r.start)
            .ToList();

        var merged = new List<(int start, int end)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.start <= merged[^1].end)
                merged[^1] = (merged[^1].start, Math.Max(merged[^1].end, range.end));
            else
                merged.Add(range);
        }

        entry.CalculatedYears = merged.Sum(r => (r.end - r.start) / 365.25);
    }

    private static List<ConsistencyIssue> FindConsistencyIssues(
        List<SkillLedgerEntry> entries, ResumeDocument resume)
    {
        var issues = new List<ConsistencyIssue>();

        // Issue 1: Skills listed but never mentioned in experience
        foreach (var entry in entries.Where(e =>
            e.Evidence.All(ev => ev.Source == EvidenceSource.SkillsSection) && e.Evidence.Count == 1))
        {
            issues.Add(new ConsistencyIssue
            {
                SkillName = entry.SkillName,
                Description = $"'{entry.SkillName}' is listed in skills but not evidenced in any experience entry",
                Severity = ConsistencySeverity.Warning
            });
        }

        // Issue 2: Claimed years don't match calculated years
        // (requires the resume to explicitly claim years, e.g., "10+ years C#")
        if (resume.Personal.Summary is not null)
        {
            var summary = resume.Personal.Summary.ToLowerInvariant();
            foreach (var entry in entries.Where(e => e.CalculatedYears > 0))
            {
                var skillLower = entry.SkillName.ToLowerInvariant();
                // Look for "X+ years of SKILL" pattern
                var idx = summary.IndexOf(skillLower, StringComparison.Ordinal);
                if (idx < 0) continue;

                // Search backwards for a number
                var before = idx > 30 ? summary[(idx - 30)..idx] : summary[..idx];
                var numbers = System.Text.RegularExpressions.Regex.Matches(before, @"(\d+)\+?\s*years?");
                if (numbers.Count > 0)
                {
                    var claimed = int.Parse(numbers[^1].Groups[1].Value);
                    if (entry.CalculatedYears < claimed * 0.7) // more than 30% discrepancy
                    {
                        issues.Add(new ConsistencyIssue
                        {
                            SkillName = entry.SkillName,
                            Description = $"Claims {claimed}+ years of {entry.SkillName} but evidence shows ~{entry.CalculatedYears:F1} years",
                            Severity = ConsistencySeverity.Error
                        });
                    }
                }
            }
        }

        // Issue 3: Stale skills — not used in recent roles
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var entry in entries.Where(e =>
            e.LastSeen.HasValue && !e.IsCurrent &&
            (today.DayNumber - e.LastSeen.Value.DayNumber) > 1825)) // > 5 years ago
        {
            issues.Add(new ConsistencyIssue
            {
                SkillName = entry.SkillName,
                Description = $"'{entry.SkillName}' last used {entry.LastSeen:MMM yyyy} — may be stale",
                Severity = ConsistencySeverity.Info
            });
        }

        return issues;
    }
}
