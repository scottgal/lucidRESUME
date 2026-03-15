using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Interfaces;

public interface IJobSpecParser
{
    Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default);
    Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default);
}
