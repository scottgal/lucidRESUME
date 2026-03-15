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
    string Description);

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
        SelectedJobDescription = value?.Description ?? "";
        TailorSelectedJobCommand.NotifyCanExecuteChanged();
        _ = RefreshAspectsAsync();
    }

    private async Task RefreshAspectsAsync()
    {
        if (SelectedJob is null)
        {
            SelectedJobAspects = [];
            return;
        }

        try
        {
            var state = await _store.LoadAsync();
            var job = BuildJobDescription(SelectedJob);
            var scored = _voteService.GetScoredAspects(job, state.Profile);
            SelectedJobAspects = scored
                .Select(a => new AspectVoteItem(
                    a.Aspect.Type,
                    AspectLabel(a.Aspect.Type),
                    a.Aspect.Value,
                    a.CurrentScore))
                .ToList()
                .AsReadOnly();
        }
        catch
        {
            SelectedJobAspects = [];
        }
    }

    [RelayCommand]
    private async Task VoteUpAsync(AspectVoteItem item)
    {
        var state = await _store.LoadAsync();
        _voteService.VoteUp(state.Profile, item.Type, item.Value);
        await _store.SaveAsync(state);
        await RefreshAspectsAsync();
    }

    [RelayCommand]
    private async Task VoteDownAsync(AspectVoteItem item)
    {
        var state = await _store.LoadAsync();
        _voteService.VoteDown(state.Profile, item.Type, item.Value);
        await _store.SaveAsync(state);
        await RefreshAspectsAsync();
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
                    Description: job.RawText));
            }

            Jobs = items;
            StatusMessage = results.Count == 0 ? "No results found." : $"Found {results.Count} jobs.";
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
        _applyPage.SetContext(state.Resume, BuildJobDescription(SelectedJob));
        NavigateTo?.Invoke("Apply");
    }

    private bool HasSelectedJob() => SelectedJob is not null;

    private static JobDescription BuildJobDescription(JobListItem item) => new()
    {
        JobId = item.JobId,
        CreatedAt = DateTimeOffset.UtcNow,
        Title = item.Title,
        Company = item.Company,
        Location = item.Location,
        IsRemote = item.IsRemote,
        RawText = item.Description
    };

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
