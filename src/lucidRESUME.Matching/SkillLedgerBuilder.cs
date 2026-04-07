using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
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

        // 1a. Collect skills from the Skills section
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

        // 1b. Add NER-extracted skills BEFORE experience scanning
        // so experience evidence gets attached to NER-discovered skills too
        foreach (var entity in resume.Entities.Where(e => e.Classification is "NerSkill" && e.Value.Length >= 2))
        {
            var entry = GetOrCreate(entries, entity.Value);
            if (!entry.Evidence.Any(e => e.Source == EvidenceSource.NerExtracted))
            {
                entry.Evidence.Add(new SkillEvidence
                {
                    SourceText = entity.Value,
                    Source = EvidenceSource.NerExtracted,
                    Confidence = entity.Confidence
                });
            }
        }

        // Pre-embed all known skill names (from Skills section + NER) for semantic matching
        var skillEmbeddings = new Dictionary<string, float[]>();
        foreach (var skillName in entries.Keys.ToList())
        {
            try { skillEmbeddings[skillName] = await _embedder.EmbedAsync(skillName, ct); }
            catch { /* embedding failure is non-fatal */ }
        }

        // 2. Scan experience entries for skill mentions
        foreach (var exp in resume.Experience)
        {
            // Technologies field - direct skill listing
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

            // Achievement bullets - semantic + substring matching
            foreach (var achievement in exp.Achievements)
            {
                var matchedSkills = await FindSkillMentionsAsync(
                    achievement, entries.Keys.ToList(), skillEmbeddings, ct);
                foreach (var (skillName, confidence) in matchedSkills)
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
                        Confidence = confidence
                    });
                }
            }
        }

        // 3. Scan summary for skill mentions (semantic)
        if (!string.IsNullOrEmpty(resume.Personal.Summary))
        {
            var summaryMatches = await FindSkillMentionsAsync(
                resume.Personal.Summary, entries.Keys.ToList(), skillEmbeddings, ct);
            foreach (var (skillName, _) in summaryMatches)
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

        // 4. (NER skills already added in step 1b - no duplicate needed)

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

    /// <summary>
    /// Applies user corrections: dismissals, renames, manual additions.
    /// Call after BuildAsync + MergeEvidence to layer user intent on top of extracted data.
    /// </summary>
    public static void ApplyOverrides(SkillLedger ledger, UserOverrides overrides)
    {
        // 1. Remove dismissed skills
        ledger.Entries.RemoveAll(e => overrides.DismissedSkills.Contains(e.SkillName));

        // 2. Remove dismissed evidence
        foreach (var dismissed in overrides.DismissedEvidence)
        {
            var entry = ledger.Find(dismissed.SkillName);
            if (entry == null) continue;
            entry.Evidence.RemoveAll(e =>
                e.Source == dismissed.Source &&
                e.ExperienceId == dismissed.ExperienceId &&
                (dismissed.SourceText == null || e.SourceText == dismissed.SourceText));
            // Remove entry entirely if no evidence remains
            if (entry.Evidence.Count == 0)
                ledger.Entries.Remove(entry);
            else
                CalculateYears(entry);
        }

        // 3. Apply renames
        foreach (var (oldName, newName) in overrides.SkillRenames)
        {
            var entry = ledger.Find(oldName);
            if (entry != null)
            {
                var existing = ledger.Find(newName);
                if (existing != null && existing != entry)
                {
                    // Merge into existing entry with the new name
                    existing.Evidence.AddRange(entry.Evidence);
                    CalculateYears(existing);
                    ledger.Entries.Remove(entry);
                }
                else
                {
                    entry.SkillName = newName;
                }
            }
        }

        // 4. Add manual skills
        foreach (var manual in overrides.ManualSkills)
        {
            var entry = ledger.Find(manual.SkillName);
            if (entry == null)
            {
                entry = new SkillLedgerEntry
                {
                    SkillName = manual.SkillName,
                    Category = manual.Category,
                };
                ledger.Entries.Add(entry);
            }
            // Add manual evidence if not already present
            if (!entry.Evidence.Any(e => e.Source == EvidenceSource.Manual))
            {
                entry.Evidence.Add(new SkillEvidence
                {
                    SourceText = manual.Note ?? "Added manually",
                    Source = EvidenceSource.Manual,
                    Confidence = 1.0,
                });
                if (manual.YearsExperience.HasValue)
                    entry.CalculatedYears = Math.Max(entry.CalculatedYears, manual.YearsExperience.Value);
            }
        }

        ledger.Entries = ledger.Entries.OrderByDescending(e => e.Strength).ToList();
    }

    /// <summary>
    /// Merges externally-sourced skill evidence (e.g. from GitHub) into an existing ledger.
    /// Existing entries get additional evidence; new skills are added.
    /// </summary>
    public static void MergeEvidence(SkillLedger ledger, IReadOnlyList<SkillLedgerEntry> externalEntries)
    {
        foreach (var external in externalEntries)
        {
            var existing = ledger.Find(external.SkillName);
            if (existing != null)
            {
                existing.Evidence.AddRange(external.Evidence);
                existing.Category ??= external.Category;
                CalculateYears(existing);
            }
            else
            {
                ledger.Entries.Add(external);
            }
        }
        ledger.Entries = ledger.Entries.OrderByDescending(e => e.Strength).ToList();
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

    /// <summary>
    /// Hybrid skill matching: substring (fast, exact) + embedding similarity (semantic).
    /// Returns matched skills with confidence scores.
    /// "K8s" in text matches "Kubernetes" in skills via embedding similarity.
    /// </summary>
    private async Task<List<(string skill, double confidence)>> FindSkillMentionsAsync(
        string text, List<string> knownSkills,
        Dictionary<string, float[]> skillEmbeddings, CancellationToken ct)
    {
        var matches = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var lower = text.ToLowerInvariant();

        // Fast pass: exact substring matching (high confidence)
        foreach (var skill in knownSkills.Where(s => s.Length >= 2))
        {
            if (lower.Contains(skill.ToLowerInvariant()))
                matches.TryAdd(skill, 0.85);
        }

        // Semantic pass: embed the text and compare against skill embeddings
        // Only for skills NOT already matched by substring
        if (skillEmbeddings.Count > 0 && text.Length > 20)
        {
            try
            {
                var textEmb = await _embedder.EmbedAsync(text, ct);
                foreach (var (skill, emb) in skillEmbeddings)
                {
                    if (matches.ContainsKey(skill)) continue; // already matched
                    var sim = _embedder.CosineSimilarity(textEmb, emb);
                    if (sim >= 0.75f) // semantic match threshold
                        matches.TryAdd(skill, sim * 0.8); // slightly lower confidence than exact
                }
            }
            catch { /* embedding failure non-fatal */ }
        }

        return matches.Select(kv => (kv.Key, kv.Value)).ToList();
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

        // Issue 3: Stale skills - not used in recent roles
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var entry in entries.Where(e =>
            e.LastSeen.HasValue && !e.IsCurrent &&
            (today.DayNumber - e.LastSeen.Value.DayNumber) > 1825)) // > 5 years ago
        {
            issues.Add(new ConsistencyIssue
            {
                SkillName = entry.SkillName,
                Description = $"'{entry.SkillName}' last used {entry.LastSeen:MMM yyyy} - may be stale",
                Severity = ConsistencySeverity.Info
            });
        }

        return issues;
    }
}