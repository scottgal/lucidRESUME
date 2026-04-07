using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Core.Models.Profile;

/// <summary>
/// User corrections layered on top of extracted data.
/// Stored separately from ResumeDocument so re-importing doesn't lose corrections.
/// </summary>
public sealed class UserOverrides
{
    /// <summary>Skills the user has dismissed entirely (by canonical name).</summary>
    public HashSet<string> DismissedSkills { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Specific evidence entries the user has dismissed.</summary>
    public List<DismissedEvidenceRecord> DismissedEvidence { get; set; } = [];

    /// <summary>Skills the user has added manually.</summary>
    public List<ManualSkillEntry> ManualSkills { get; set; } = [];

    /// <summary>Skill name corrections: original extracted name → user-corrected name.</summary>
    public Dictionary<string, string> SkillRenames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Personal info field overrides: field name → corrected value.</summary>
    public Dictionary<string, string> PersonalInfoOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DismissedEvidenceRecord
{
    public string SkillName { get; set; } = "";
    public Guid? ExperienceId { get; set; }
    public EvidenceSource Source { get; set; }
    public string? SourceText { get; set; }
}

public sealed class ManualSkillEntry
{
    public string SkillName { get; set; } = "";
    public string? Category { get; set; }
    public double? YearsExperience { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
