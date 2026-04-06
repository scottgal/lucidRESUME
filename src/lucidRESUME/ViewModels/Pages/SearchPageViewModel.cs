using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed record SavedJobItem(
    Guid JobId,
    string Title,
    string Company,
    string Location,
    string Source,
    JobDescription FullJob);

public sealed partial class SearchPageViewModel : ViewModelBase
{
    private readonly IJobSpecParser _parser;
    private readonly IAppStore _store;

    [ObservableProperty] private string _jobUrl = "";
    [ObservableProperty] private string _pastedText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _needsManualText;
    [ObservableProperty] private string _manualUrl = "";
    [ObservableProperty] private IReadOnlyList<SavedJobItem> _savedJobs = [];

    // Search watches
    [ObservableProperty] private ObservableCollection<WatchItem> _watches = [];
    [ObservableProperty] private string _newWatchQuery = "";
    [ObservableProperty] private bool _newWatchRequireSalary;
    [ObservableProperty] private bool _newWatchRequireRemote;
    [ObservableProperty] private int _newWatchPollMinutes = 60;

    public SearchPageViewModel(IJobSpecParser parser, IAppStore store)
    {
        _parser = parser;
        _store = store;
        _ = LoadSavedJobsAsync();
        _ = LoadWatchesAsync();
    }

    [RelayCommand]
    private async Task FetchUrlAsync()
    {
        var url = JobUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        IsLoading = true;
        StatusMessage = "Fetching…";
        NeedsManualText = false;

        try
        {
            var job = await _parser.ParseFromUrlAsync(url);

            if (job.NeedsManualInput)
            {
                ManualUrl = url;
                NeedsManualText = true;
                StatusMessage = "This site requires manual paste — copy the JD text below.";
                return;
            }

            await SaveJobAsync(job);
            JobUrl = "";
            StatusMessage = $"Added: {job.Title ?? "Job"} at {job.Company ?? "Unknown"}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddTextAsync()
    {
        var text = PastedText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        IsLoading = true;
        StatusMessage = "Parsing…";

        try
        {
            var job = await _parser.ParseFromTextAsync(text);

            // If this was triggered from a manual-paste fallback, carry the URL
            if (!string.IsNullOrWhiteSpace(ManualUrl))
            {
                job = JobDescription.Create(text, new JobSource
                {
                    Type = JobSourceType.Url,
                    Url = ManualUrl
                });
                ManualUrl = "";
                NeedsManualText = false;
            }

            await SaveJobAsync(job);
            PastedText = "";
            StatusMessage = $"Added: {job.Title ?? "Job"} at {job.Company ?? "Unknown"}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Programmatic job add for UX testing — bypasses UI interaction.</summary>
    public async Task AddJobFromTextAsync(string text)
    {
        PastedText = text;
        await AddTextAsync();
    }

    [RelayCommand]
    private async Task RemoveJobAsync(SavedJobItem item)
    {
        await _store.MutateAsync(state =>
            state.Jobs.RemoveAll(j => j.JobId == item.JobId));
        await LoadSavedJobsAsync();
        StatusMessage = "Removed.";
    }

    private async Task SaveJobAsync(JobDescription job)
    {
        await _store.MutateAsync(state => state.Jobs.Add(job));
        await LoadSavedJobsAsync();
    }

    private async Task LoadSavedJobsAsync()
    {
        var state = await _store.LoadAsync();
        SavedJobs = state.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new SavedJobItem(
                j.JobId,
                j.Title ?? "(No title)",
                j.Company ?? "(Unknown)",
                j.Location ?? "",
                SourceLabel(j.Source),
                j))
            .ToList()
            .AsReadOnly();
    }

    private static string SourceLabel(JobSource source) => source.Type switch
    {
        JobSourceType.PastedText => "Pasted",
        JobSourceType.Url        => TruncateDomain(source.Url),
        _                        => source.Type.ToString()
    };

    private static string TruncateDomain(string? url)
    {
        if (url is null) return "URL";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host.Replace("www.", "");
        return "URL";
    }

    // ── Search Watches ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task CreateWatchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWatchQuery)) return;

        var watch = new SearchWatch
        {
            Name = NewWatchQuery.Length > 30 ? NewWatchQuery[..27] + "..." : NewWatchQuery,
            Query = NewWatchQuery,
            PollIntervalMinutes = NewWatchPollMinutes,
            Filters = new SearchHardFilter
            {
                RequireSalary = NewWatchRequireSalary,
                RequireRemote = NewWatchRequireRemote,
            }
        };

        await _store.MutateAsync(state => state.SearchWatches.Add(watch));
        NewWatchQuery = "";
        StatusMessage = $"Watch created: polling every {NewWatchPollMinutes}min";
        await LoadWatchesAsync();
    }

    [RelayCommand]
    private async Task DeleteWatchAsync(WatchItem item)
    {
        await _store.MutateAsync(state =>
            state.SearchWatches.RemoveAll(w => w.WatchId == item.WatchId));
        await LoadWatchesAsync();
        StatusMessage = "Watch removed.";
    }

    [RelayCommand]
    private async Task ToggleWatchAsync(WatchItem item)
    {
        await _store.MutateAsync(state =>
        {
            var w = state.SearchWatches.FirstOrDefault(sw => sw.WatchId == item.WatchId);
            if (w is not null) w.IsActive = !w.IsActive;
        });
        await LoadWatchesAsync();
    }

    private async Task LoadWatchesAsync()
    {
        var state = await _store.LoadAsync();
        Watches = new ObservableCollection<WatchItem>(
            state.SearchWatches.Select(w => new WatchItem(
                w.WatchId,
                w.Name,
                w.Query,
                w.IsActive,
                w.PollIntervalMinutes,
                w.LastPolledAt?.ToString("g") ?? "never",
                w.LastNewMatches)));
    }
}

public sealed record WatchItem(
    Guid WatchId,
    string Name,
    string Query,
    bool IsActive,
    int PollMinutes,
    string LastPolled,
    int LastMatches);
