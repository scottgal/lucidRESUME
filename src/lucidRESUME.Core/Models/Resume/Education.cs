namespace lucidRESUME.Core.Models.Resume;

public sealed class Education
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Institution { get; set; }
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public double? Gpa { get; set; }
    public int? GraduationYear { get; set; }
    public List<string> Highlights { get; set; } = [];
}
