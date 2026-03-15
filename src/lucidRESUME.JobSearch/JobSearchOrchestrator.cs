using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.JobSearch;

public sealed class JobSearchOrchestrator
{
    private readonly RoleSuggestionService _roleSuggestionService;
    private readonly JobDeduplicator _deduplicator;
    private readonly IEnumerable<IJobSearchAdapter> _adapters;
    private readonly IMatchingService _matchingService;

    public JobSearchOrchestrator(
        RoleSuggestionService roleSuggestionService,
        JobDeduplicator deduplicator,
        IEnumerable<IJobSearchAdapter> adapters,
        IMatchingService matchingService)
    {
        _roleSuggestionService = roleSuggestionService;
        _deduplicator = deduplicator;
        _adapters = adapters;
        _matchingService = matchingService;
    }

    public async Task<IReadOnlyList<JobSearchResult>> SearchAsync(
        ResumeDocument resume, UserProfile profile, CancellationToken ct = default)
    {
        // 1. Generate queries from resume + profile
        var queries = _roleSuggestionService.GenerateQueries(resume, profile);

        // 2. Fan out every query to every configured adapter in parallel
        var configuredAdapters = _adapters.Where(a => a.IsConfigured).ToList();

        var searchPairs = queries
            .SelectMany(q => configuredAdapters.Select(a => (Adapter: a, Query: q)))
            .ToList();

        var searchTasks = searchPairs.Select(async pair =>
        {
            try
            {
                var jobs = await pair.Adapter.SearchAsync(pair.Query, ct);
                return jobs;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log and degrade gracefully — one failing adapter should not crash the search
                _ = ex; // surfaced to caller via logs in the adapter; swallow here
                return (IReadOnlyList<JobDescription>)[];
            }
        });

        var allResults = await Task.WhenAll(searchTasks);

        // 3. Flatten all results
        var allJobs = allResults.SelectMany(r => r);

        // 4. Deduplicate
        var deduplicated = _deduplicator.Deduplicate(allJobs);

        // 5. Filter blocked companies (case-insensitive contains)
        var filtered = deduplicated
            .Where(j => !profile.BlockedCompanies.Any(blocked =>
                (j.Company ?? "").Contains(blocked, StringComparison.OrdinalIgnoreCase)));

        // 6. Filter blocked industries (check title + raw text for keywords)
        filtered = filtered
            .Where(j => !profile.Preferences.BlockedIndustries.Any(industry =>
                (j.Title ?? "").Contains(industry, StringComparison.OrdinalIgnoreCase) ||
                j.RawText.Contains(industry, StringComparison.OrdinalIgnoreCase)));

        var filteredList = filtered.ToList();

        // 7. Match each remaining job and wrap in JobSearchResult
        var matchTasks = filteredList.Select(async job =>
        {
            var match = await _matchingService.MatchAsync(resume, job, profile, ct);
            return new JobSearchResult
            {
                Job = job,
                Match = match,
                AdapterName = job.Source.ApiName ?? ""
            };
        });

        var results = await Task.WhenAll(matchTasks);

        // 8. Sort by score descending
        return [.. results.OrderByDescending(r => r.Score)];
    }
}
