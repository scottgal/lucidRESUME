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
            .Select(a => SafeSearchAsync(a, query, ct));

        var results = await Task.WhenAll(tasks);

        // Deduplicate by source URL (case-insensitive), preserving first occurrence
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<JobDescription>();

        foreach (var job in results.SelectMany(r => r))
        {
            var key = job.Source?.Url ?? string.Empty;
            if (key.Length == 0 || seen.Add(key))
                deduped.Add(job);
        }

        return deduped;
    }

    private static async Task<IReadOnlyList<JobDescription>> SafeSearchAsync(
        IJobSearchAdapter adapter, JobSearchQuery query, CancellationToken ct)
    {
        try
        {
            return await adapter.SearchAsync(query, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }
}
