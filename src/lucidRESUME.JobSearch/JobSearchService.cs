using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.JobSearch;

public sealed class JobSearchService
{
    private readonly IEnumerable<IJobSearchAdapter> _adapters;

    public JobSearchService(IEnumerable<IJobSearchAdapter> adapters)
        => _adapters = adapters;

    public async Task<IReadOnlyList<JobDescription>> SearchAllAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var tasks = _adapters
            .Where(a => a.IsConfigured)
            .Select(a => a.SearchAsync(query, ct));

        var results = await Task.WhenAll(tasks);
        return [.. results.SelectMany(r => r)];
    }
}
