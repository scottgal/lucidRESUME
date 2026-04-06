using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSearch;

/// <summary>
/// Polls due SearchWatches, fetches new jobs, filters, and returns notifications.
/// Designed to be called periodically (e.g., every 5 minutes from a timer).
/// </summary>
public sealed class SearchWatchPoller
{
    private readonly JobSearchService _searchService;
    private readonly IAppStore _store;
    private readonly ILogger<SearchWatchPoller> _logger;

    public SearchWatchPoller(JobSearchService searchService, IAppStore store,
        ILogger<SearchWatchPoller> logger)
    {
        _searchService = searchService;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Poll all due watches. Returns new matches found since last poll.
    /// </summary>
    public async Task<IReadOnlyList<WatchNotification>> PollDueWatchesAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        var dueWatches = state.SearchWatches.Where(w => w.IsDue).ToList();

        if (dueWatches.Count == 0) return [];

        _logger.LogInformation("Polling {Count} due search watches", dueWatches.Count);
        var notifications = new List<WatchNotification>();

        var existingJobIds = new HashSet<Guid>(state.Jobs.Select(j => j.JobId));
        var decayTracker = new JobDecayTracker();

        foreach (var watch in dueWatches)
        {
            try
            {
                var query = new JobSearchQuery(watch.Query);
                var results = await _searchService.SearchAllAsync(query, ct);

                // Apply hard filters + dedup against existing jobs
                var newMatches = results
                    .Where(j => !existingJobIds.Contains(j.JobId))
                    .Where(j => watch.Filters.Passes(j))
                    .Where(j => decayTracker.GetFreshness(j) != JobFreshness.Expired)
                    .Where(j => !decayTracker.IsCompanyOnCooldown(j.Company ?? "", state.Applications))
                    .ToList();

                // Update watch state
                await _store.MutateAsync(s =>
                {
                    var w = s.SearchWatches.FirstOrDefault(sw => sw.WatchId == watch.WatchId);
                    if (w is null) return;
                    w.LastPolledAt = DateTimeOffset.UtcNow;
                    w.LastNewMatches = newMatches.Count;

                    // Save new jobs to the store
                    foreach (var job in newMatches)
                    {
                        if (!s.Jobs.Any(j => j.JobId == job.JobId))
                            s.Jobs.Add(job);
                    }
                }, ct);

                if (newMatches.Count > 0)
                {
                    notifications.Add(new WatchNotification
                    {
                        WatchName = watch.Name,
                        Query = watch.Query,
                        NewJobCount = newMatches.Count,
                        TopJobs = newMatches
                            .Take(3)
                            .Select(j => $"{j.Title ?? "?"} at {j.Company ?? "?"}")
                            .ToList(),
                    });
                }

                _logger.LogInformation("Watch '{Name}': {New} new jobs from '{Query}'",
                    watch.Name, newMatches.Count, watch.Query);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watch '{Name}' poll failed", watch.Name);
            }
        }

        return notifications;
    }
}

public sealed class WatchNotification
{
    public string WatchName { get; init; } = "";
    public string Query { get; init; } = "";
    public int NewJobCount { get; init; }
    public List<string> TopJobs { get; init; } = [];
}
