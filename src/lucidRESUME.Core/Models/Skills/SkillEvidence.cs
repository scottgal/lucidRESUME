namespace lucidRESUME.Core.Models.Skills;

/// <summary>
/// A single piece of evidence that a person has a particular skill.
/// Links back to the source: which job, which bullet, what date range.
/// </summary>
public sealed class SkillEvidence
{
    /// <summary>The experience entry where this skill was evidenced.</summary>
    public Guid? ExperienceId { get; set; }

    /// <summary>Company name (denormalized for display).</summary>
    public string? Company { get; set; }

    /// <summary>Job title at the time.</summary>
    public string? JobTitle { get; set; }

    /// <summary>The specific text that evidences the skill.</summary>
    public string SourceText { get; set; } = "";

    /// <summary>Start of the date range where this skill was used.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>End of the date range (null = current/present).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>How this evidence was detected.</summary>
    public EvidenceSource Source { get; set; }

    /// <summary>Confidence that this text actually evidences the skill (0-1).</summary>
    public double Confidence { get; set; }
}

public enum EvidenceSource
{
    SkillsSection,      // listed in a Skills/Competencies section
    AchievementBullet,  // mentioned in an experience achievement
    JobTechnology,      // listed in Technologies field of an experience entry
    Summary,            // mentioned in the summary/profile section
    Education,          // mentioned in education (coursework, thesis)
    NerExtracted,       // detected by NER model
    LlmExtracted,       // recovered by LLM
    GitHubRepository,   // extracted from GitHub repo languages/topics
    Manual,             // added by the user directly
}
