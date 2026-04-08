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

    /// <summary>
    /// Normalised education level — cross-cultural equivalence.
    /// Auto-classified from Degree string. A "BSc (Hons)" and "B.Tech" both → Bachelors.
    /// </summary>
    public EducationLevel Level { get; set; } = EducationLevel.Unknown;

    /// <summary>Import sources that contributed to this entry.</summary>
    public List<string> ImportSources { get; set; } = [];

    /// <summary>Auto-classify the education level from the degree string.</summary>
    public void ClassifyLevel()
    {
        if (Level == EducationLevel.Unknown && !string.IsNullOrWhiteSpace(Degree))
            Level = EducationLevelClassifier.Classify(Degree);
        // Also try field of study (e.g. "laser physics and optoelectronics" at postgrad level)
        if (Level == EducationLevel.Unknown && !string.IsNullOrWhiteSpace(FieldOfStudy))
            Level = EducationLevelClassifier.Classify(FieldOfStudy);
    }
}
