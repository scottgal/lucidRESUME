namespace lucidRESUME.Core.Models.Resume;

public sealed class WorkExperience
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Company { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>The source supplied an end month, not an exact day (for example LinkedIn).</summary>
    public bool EndDateIsMonthOnly { get; set; }
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Render this ledger-backed role as a one-line chronology entry. Used for
    /// qualifying roles omitted from the detailed, vacancy-specific projection.
    /// </summary>
    public bool IsCompact { get; set; }

    /// <summary>
    /// Author-controlled publication preference. Career anchors are retained in every
    /// role-specific resume projection even when age or lexical similarity would
    /// otherwise push them outside the detailed-role budget.
    /// </summary>
    public bool IsCareerAnchor { get; set; }

    public List<string> Achievements { get; set; } = [];
    public List<RoleInterviewEntry> InterviewEntries { get; set; } = [];
    public List<string> Technologies { get; set; } = [];

    /// <summary>Import sources that contributed to this entry (e.g. "executive-resume.docx", "LinkedIn").</summary>
    public List<string> ImportSources { get; set; } = [];
}
