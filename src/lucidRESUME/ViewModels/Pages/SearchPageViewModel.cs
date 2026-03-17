using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
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

    public SearchPageViewModel(IJobSpecParser parser, IAppStore store)
    {
        _parser = parser;
        _store = store;
        _ = LoadSavedJobsAsync();
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
}
