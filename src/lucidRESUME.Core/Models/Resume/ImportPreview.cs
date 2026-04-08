namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Preview of what an import would change, BEFORE it's applied.
/// Every item has an IsAccepted flag the user can toggle.
/// </summary>
public sealed class ImportPreview
{
    public string SourceName { get; init; } = "";
    public ResumeDocument Incoming { get; init; } = new();

    // Personal info: what would change
    public List<FieldChange> PersonalInfoChanges { get; init; } = [];

    // Experience: new entries and merge proposals
    public List<ReviewableItem<WorkExperience>> NewExperience { get; init; } = [];
    public List<ExperienceMergePreview> MergedExperience { get; init; } = [];

    // Skills
    public List<ReviewableItem<Skill>> NewSkills { get; init; } = [];
    public List<SkillUpdatePreview> UpdatedSkills { get; init; } = [];

    // Education
    public List<ReviewableItem<Education>> NewEducation { get; init; } = [];

    // Projects
    public List<ReviewableItem<Project>> NewProjects { get; init; } = [];

    // Anomalies detected
    public List<ImportAnomaly> Anomalies { get; init; } = [];

    // Stats
    public int TotalNewItems => NewExperience.Count + NewSkills.Count + NewEducation.Count + NewProjects.Count;
    public int TotalMergeItems => MergedExperience.Count + UpdatedSkills.Count;
    public int TotalAnomalies => Anomalies.Count;
}

/// <summary>Wraps any item with an accept/reject toggle.</summary>
public sealed class ReviewableItem<T>
{
    public T Item { get; init; } = default!;
    public bool IsAccepted { get; set; } = true; // default: accept
    public string? Note { get; set; } // optional reason for rejection
}

/// <summary>A personal info field that would change.</summary>
public sealed class FieldChange
{
    public string FieldName { get; init; } = "";
    public string? CurrentValue { get; init; }
    public string? IncomingValue { get; init; }
    public bool IsConflict { get; init; } // true if both have different non-null values
    public bool IsAccepted { get; set; } = true;
}

/// <summary>Preview of merging two experience entries.</summary>
public sealed class ExperienceMergePreview
{
    public WorkExperience Existing { get; init; } = new();
    public WorkExperience Incoming { get; init; } = new();
    public bool TitleDiffers { get; init; }
    public bool DatesDiffer { get; init; }
    public int NewAchievementsCount { get; init; }
    public int NewTechnologiesCount { get; init; }
    public bool IsAccepted { get; set; } = true;
}

/// <summary>Preview of updating an existing skill with new data.</summary>
public sealed class SkillUpdatePreview
{
    public Skill Existing { get; init; } = new();
    public Skill Incoming { get; init; } = new();
    public bool EndorsementChanged { get; init; }
    public bool YearsChanged { get; init; }
    public bool IsAccepted { get; set; } = true;
}
