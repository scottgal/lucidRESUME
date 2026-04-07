using lucidRESUME.Core.Models.Resume;
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

    /// <summary>
    /// When merging experience entries from multiple resume imports, both variants
    /// are stored here. Key is the merged experience's Id. User can pick preferred wording.
    /// </summary>
    public List<ExperienceVariant> ExperienceVariants { get; set; } = [];

    /// <summary>User's preferred variant per experience entry (Id → chosen variant index).</summary>
    public Dictionary<string, int> SelectedVariant { get; set; } = [];
}

public sealed class DismissedEvidenceRecord
{
    public string SkillName { get; set; } = "";
    public Guid? ExperienceId { get; set; }
    public EvidenceSource Source { get; set; }
    public string? SourceText { get; set; }
}

/// <summary>
/// Stores alternate wordings for the same experience entry from different imports.
/// E.g. LinkedIn says "Contract Lead Developer" while the DOCX says "Lead Contract Developer".
/// </summary>
public sealed class ExperienceVariant
{
    public Guid ExperienceId { get; set; }
    public List<WorkExperience> Variants { get; set; } = [];
    public string Source { get; set; } = ""; // which import each came from
}

public sealed class ManualSkillEntry
{
    public string SkillName { get; set; } = "";
    public string? Category { get; set; }
    public double? YearsExperience { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
