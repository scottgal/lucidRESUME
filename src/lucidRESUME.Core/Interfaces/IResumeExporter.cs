using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public enum ExportFormat { JsonResume, Pdf, Docx, Markdown }

public interface IResumeExporter
{
    ExportFormat Format { get; }
    Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default);
}
