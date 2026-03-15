using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Persistence;
using lucidRESUME.JobSearch;
using lucidRESUME.Matching;

namespace lucidRESUME.ViewModels.Pages;

public sealed record JobListItem(
    Guid JobId,
    string Title,
    string Company,
    string Location,
    bool IsRemote,
    double SkillScore,
    string SourceUrl,
    // Carry the full job so aspect extraction and tailoring have structured fields
    JobDescription FullJob);

/// <summary>A single votable aspect shown in the job detail panel.</summary>
public sealed record AspectVoteItem(
    AspectType Type,
    string TypeLabel,
    string Value,
    int Score);

public sealed partial class JobsPageViewModel : ViewModelBase
{
    private readonly JobSearchService _jobSearchService;
    private readonly IMatchingService _matchingService;
    private readonly VoteService _voteService;
    private readonly IAppStore _store;
    private readonly ApplyPageViewModel _applyPage;

    // Cancels any in-flight RefreshAspectsAsync when the selected job changes
    private CancellationTokenSource? _refreshCts;

    /// <summary>Set after construction to allow navigation to the Apply page.</summary>
    public Action<string>? NavigateTo { get; set; }

    [ObservableProperty] private IReadOnlyList<JobListItem> _jobs = [];
    [ObservableProperty] private JobListItem? _selectedJob;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _searchQuery = "";

    // Flat properties for the detail panel (avoids nullable drill-through in compiled bindings)
    [ObservableProperty] private string _selectedJobTitle = "";
    [ObservableProperty] private string _selectedJobCompany = "";
    [ObservableProperty] private string _selectedJobLocation = "";
    [ObservableProperty] private bool _selectedJobIsRemote;
    [ObservableProperty] private string _selectedJobDescription = "";

    [ObservableProperty] private IReadOnlyList<AspectVoteItem> _selectedJobAspects = [];

    public JobsPageViewModel(
        JobSearchService jobSearchService,
        IMatchingService matchingService,
        VoteService voteService,
        IAppStore store,
        ApplyPageViewModel applyPage)
    {
        _jobSearchService = jobSearchService;
        _matchingService = matchingService;
        _voteService = voteService;
        _store = store;
        _applyPage = applyPage;
    }

    partial void OnSelectedJobChanged(JobListItem? value)
    {
        SelectedJobTitle = value?.Title ?? "";
        SelectedJobCompany = value?.Company ?? "";
        SelectedJobLocation = value?.Location ?? "";
        SelectedJobIsRemote = value?.IsRemote ?? false;
        SelectedJobDescription = value?.FullJob.RawText ?? "";
        TailorSelectedJobCommand.NotifyCanExecuteChanged();

        // Cancel previous refresh and start a fresh one
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        _ = RefreshAspectsAsync(_refreshCts.Token);
    }

    private async Task RefreshAspectsAsync(CancellationToken ct)
    {
        if (SelectedJob is null)
        {
            SelectedJobAspects = [];
            return;
        }

        try
        {
            var state = await _store.LoadAsync(ct);
            ct.ThrowIfCancellationRequested();

            var scored = _voteService.GetScoredAspects(SelectedJob.FullJob, state.Profile);
            SelectedJobAspects = scored
                .Select(a => new AspectVoteItem(
                    a.Aspect.Type,
                    AspectLabel(a.Aspect.Type),
                    a.Aspect.Value,
                    a.CurrentScore))
                .ToList()
                .AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            // Selection changed before we finished — discard silently
        }
        catch
        {
            SelectedJobAspects = [];
        }
    }

    [RelayCommand]
    private async Task VoteUpAsync(AspectVoteItem item)
    {
        await _store.MutateAsync(state => _voteService.VoteUp(state.Profile, item.Type, item.Value));
        await RefreshAspectsAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task VoteDownAsync(AspectVoteItem item)
    {
        await _store.MutateAsync(state => _voteService.VoteDown(state.Profile, item.Type, item.Value));
        await RefreshAspectsAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsLoading = true;
        StatusMessage = "Searching…";
        SelectedJob = null;

        try
        {
            var query = new JobSearchQuery(SearchQuery, MaxResults: 20);
            var results = await _jobSearchService.SearchAllAsync(query);

            var state = await _store.LoadAsync();
            var resume = state.Resume;
            var profile = state.Profile;

            var items = new List<JobListItem>();
            foreach (var job in results)
            {
                double score = 0.0;
                if (resume is not null)
                {
                    try
                    {
                        var match = await _matchingService.MatchAsync(resume, job, profile);
                        score = match.Score;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // propagate cancellation
                    }
                    catch
                    {
                        score = 0.0;
                    }
                }

                items.Add(new JobListItem(
                    JobId: job.JobId,
                    Title: job.Title ?? "(No title)",
                    Company: job.Company ?? "(Unknown)",
                    Location: job.Location ?? "",
                    IsRemote: job.IsRemote ?? false,
                    SkillScore: score,
                    SourceUrl: job.Source?.Url ?? "",
                    FullJob: job));
            }

            Jobs = items;
            StatusMessage = results.Count == 0 ? "No results found." : $"Found {results.Count} jobs.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectJob(JobListItem item) => SelectedJob = item;

    [RelayCommand(CanExecute = nameof(HasSelectedJob))]
    private async Task TailorSelectedJobAsync()
    {
        if (SelectedJob is null) return;
        var state = await _store.LoadAsync();
        _applyPage.SetContext(state.Resume, SelectedJob.FullJob);
        NavigateTo?.Invoke("Apply");
    }

    private bool HasSelectedJob() => SelectedJob is not null;

    private static string AspectLabel(AspectType type) => type switch
    {
        AspectType.Skill         => "Skill",
        AspectType.WorkModel     => "Work Model",
        AspectType.CompanyType   => "Company Type",
        AspectType.Industry      => "Industry",
        AspectType.SalaryBand    => "Salary",
        AspectType.CultureSignal => "Culture",
        _                        => type.ToString()
    };
}
