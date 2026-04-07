using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Compares skill ledgers across resume variants to detect temporal drift:
/// which skills were added, dropped, strengthened, or weakened over time.
/// </summary>
public static class SkillDriftAnalyser
{
    /// <summary>
    /// Compare two ledgers (earlier vs later) and produce a drift report.
    /// </summary>
    public static SkillDriftReport Compare(SkillLedger earlier, SkillLedger later)
    {
        var report = new SkillDriftReport();

        var earlierSkills = earlier.Entries.ToDictionary(e => e.SkillName, StringComparer.OrdinalIgnoreCase);
        var laterSkills = later.Entries.ToDictionary(e => e.SkillName, StringComparer.OrdinalIgnoreCase);

        // Skills in later but not earlier = added
        foreach (var (name, entry) in laterSkills)
        {
            if (!earlierSkills.ContainsKey(name))
            {
                report.Added.Add(new SkillDrift
                {
                    SkillName = name,
                    Category = entry.Category,
                    NewStrength = entry.Strength,
                    NewYears = entry.CalculatedYears,
                    EvidenceCount = entry.Evidence.Count,
                });
            }
        }

        // Skills in earlier but not later = dropped
        foreach (var (name, entry) in earlierSkills)
        {
            if (!laterSkills.ContainsKey(name))
            {
                report.Dropped.Add(new SkillDrift
                {
                    SkillName = name,
                    Category = entry.Category,
                    OldStrength = entry.Strength,
                    OldYears = entry.CalculatedYears,
                });
            }
        }

        // Skills in both = check for strength/years changes
        foreach (var (name, laterEntry) in laterSkills)
        {
            if (!earlierSkills.TryGetValue(name, out var earlierEntry)) continue;

            var strengthDelta = laterEntry.Strength - earlierEntry.Strength;
            var yearsDelta = laterEntry.CalculatedYears - earlierEntry.CalculatedYears;

            if (Math.Abs(strengthDelta) < 0.05 && Math.Abs(yearsDelta) < 0.5)
                continue; // negligible change

            var drift = new SkillDrift
            {
                SkillName = name,
                Category = laterEntry.Category,
                OldStrength = earlierEntry.Strength,
                NewStrength = laterEntry.Strength,
                OldYears = earlierEntry.CalculatedYears,
                NewYears = laterEntry.CalculatedYears,
                EvidenceCount = laterEntry.Evidence.Count,
            };

            if (strengthDelta > 0.05)
                report.Strengthened.Add(drift);
            else if (strengthDelta < -0.05)
                report.Weakened.Add(drift);
            else if (yearsDelta > 0.5)
                report.Strengthened.Add(drift);
            else
                report.Weakened.Add(drift);
        }

        // Sort by magnitude of change
        report.Added = report.Added.OrderByDescending(d => d.NewStrength).ToList();
        report.Dropped = report.Dropped.OrderByDescending(d => d.OldStrength).ToList();
        report.Strengthened = report.Strengthened.OrderByDescending(d => d.StrengthDelta).ToList();
        report.Weakened = report.Weakened.OrderBy(d => d.StrengthDelta).ToList();

        return report;
    }
}

public sealed class SkillDriftReport
{
    public List<SkillDrift> Added { get; set; } = [];
    public List<SkillDrift> Dropped { get; set; } = [];
    public List<SkillDrift> Strengthened { get; set; } = [];
    public List<SkillDrift> Weakened { get; set; } = [];

    public int TotalChanges => Added.Count + Dropped.Count + Strengthened.Count + Weakened.Count;
}

public sealed class SkillDrift
{
    public string SkillName { get; init; } = "";
    public string? Category { get; init; }
    public double OldStrength { get; init; }
    public double NewStrength { get; init; }
    public double OldYears { get; init; }
    public double NewYears { get; init; }
    public int EvidenceCount { get; init; }

    public double StrengthDelta => NewStrength - OldStrength;
    public double YearsDelta => NewYears - OldYears;
}
