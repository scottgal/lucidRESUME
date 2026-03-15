using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Interfaces;

public record JobSearchQuery(string Keywords, string? Location = null, bool? RemoteOnly = null, int MaxResults = 20);

public interface IJobSearchAdapter
{
    string AdapterName { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default);
}
