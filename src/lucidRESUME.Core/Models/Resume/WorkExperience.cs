namespace lucidRESUME.Core.Models.Resume;

public sealed class WorkExperience
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Company { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public List<string> Achievements { get; set; } = [];
    public List<string> Technologies { get; set; } = [];

    /// <summary>Import sources that contributed to this entry (e.g. "Scott_Galloway_CTO.docx", "LinkedIn").</summary>
    public List<string> ImportSources { get; set; } = [];
}
