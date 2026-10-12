namespace lucidRESUME.Core.Models.Resume;

public enum ResumeLength { Automatic, Concise, Standard, Extended }
public enum ResumeDetail { Balanced, Brief, Thorough }
public enum ResumeTone { Direct, Understated, Expressive }

/// <summary>Evidence-selection preferences, retained with each generated version.</summary>
public sealed record ResumeGenerationOptions
{
    public ResumeLength Length { get; init; } = ResumeLength.Automatic;
    public ResumeDetail Detail { get; init; } = ResumeDetail.Balanced;
    public ResumeTone Tone { get; init; } = ResumeTone.Direct;
}
