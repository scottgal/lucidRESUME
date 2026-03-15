using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface IResumeParser
{
    Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default);
}
