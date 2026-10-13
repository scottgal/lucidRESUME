using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Interfaces;

public interface IJobSpecParser
{
    Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default);
    Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default);

    /// <summary>Refreshes stale saved requirements without changing the job identity or user-edited metadata.</summary>
    Task<bool> EnsureRequirementsCurrentAsync(JobDescription job, CancellationToken ct = default) => Task.FromResult(false);
}
