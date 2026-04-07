namespace lucidRESUME.Core.Models.Skills;

/// <summary>
/// A single skill in the ledger with all evidence, calculated years, and consistency info.
/// This is the "source of truth" for what skills the person actually has evidence for.
/// </summary>
public sealed class SkillLedgerEntry
{
    /// <summary>Canonical skill name (normalized - "K8s" → "Kubernetes").</summary>
    public string SkillName { get; set; } = "";

    /// <summary>Category if known (e.g., "Programming Language", "Cloud", "Framework").</summary>
    public string? Category { get; set; }

    /// <summary>All evidence sources for this skill, ordered by date.</summary>
    public List<SkillEvidence> Evidence { get; set; } = [];

    /// <summary>Total years of experience calculated from evidence date ranges.</summary>
    public double CalculatedYears { get; set; }

    /// <summary>Earliest date this skill appears in the resume.</summary>
    public DateOnly? FirstSeen { get; set; }

    /// <summary>Latest date this skill appears (null = current).</summary>
    public DateOnly? LastSeen { get; set; }

    /// <summary>Whether the skill is still current (appeared in a current role).</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Number of distinct roles/jobs where this skill was evidenced.</summary>
    public int RoleCount => Evidence.Select(e => e.ExperienceId).Distinct().Count();

    /// <summary>Highest confidence evidence for this skill.</summary>
    public double MaxConfidence => Evidence.Count > 0 ? Evidence.Max(e => e.Confidence) : 0;

    /// <summary>
    /// Strength score (0-1) combining years, role count, recency, and confidence.
    /// Used for ranking and gap analysis.
    /// </summary>
    public double Strength
    {
        get
        {
            var yearsFactor = Math.Min(CalculatedYears / 10.0, 1.0); // cap at 10 years
            var rolesFactor = Math.Min(RoleCount / 5.0, 1.0); // cap at 5 roles
            var recencyFactor = IsCurrent ? 1.0 : LastSeen.HasValue
                ? Math.Max(0, 1.0 - (DateOnly.FromDateTime(DateTime.Today).DayNumber - LastSeen.Value.DayNumber) / 1825.0) // decay over 5 years
                : 0.3;
            var confFactor = MaxConfidence;

            return yearsFactor * 0.3 + rolesFactor * 0.25 + recencyFactor * 0.25 + confFactor * 0.2;
        }
    }
}