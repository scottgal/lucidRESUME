using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>An immutable application version, separate from the career inputs used to generate it.</summary>
public sealed class GeneratedResume
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public JobDescription Job { get; set; } = new();
    public ResumeDocument Resume { get; set; } = new();
    public ResumeDocument Source { get; set; } = new();
    public string Label => $"{Job.Title ?? "Resume"} · {Job.Company ?? "Unspecified company"}";
    public string Detail => $"{CreatedAt.ToLocalTime():dd MMM yyyy HH:mm} · {Resume.OutputTemplateId}";
}
