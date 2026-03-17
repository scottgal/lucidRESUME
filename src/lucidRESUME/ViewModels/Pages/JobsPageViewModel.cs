using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Quality;
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

/// <summary>A single required-skill coverage row in the job detail panel.</summary>
public sealed record CoverageItemViewModel(
    string Requirement,
    bool IsCovered,
    string? Evidence,
    string StatusColor);

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
    private readonly IJobQualityAnalyser _jobQualityAnalyser;
    private readonly ICoverageAnalyser _coverageAnalyser;
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

    // JD quality banner
    [ObservableProperty] private bool _hasJdQualityReport;
    [ObservableProperty] private int _jdQualityScore;
    [ObservableProperty] private IReadOnlyList<QualityFindingViewModel> _jdQualityFindings = [];

    // Coverage panel
    [ObservableProperty] private bool _hasCoverageReport;
    [ObservableProperty] private int _coveragePercent;
    [ObservableProperty] private IReadOnlyList<CoverageItemViewModel> _coverageItems = [];

    public JobsPageViewModel(
        JobSearchService jobSearchService,
        IMatchingService matchingService,
        VoteService voteService,
        IJobQualityAnalyser jobQualityAnalyser,
        ICoverageAnalyser coverageAnalyser,
        IAppStore store,
        ApplyPageViewModel applyPage)
    {
        _jobSearchService = jobSearchService;
        _matchingService = matchingService;
        _voteService = voteService;
        _jobQualityAnalyser = jobQualityAnalyser;
        _coverageAnalyser = coverageAnalyser;
        _store = store;
        _applyPage = applyPage;
        _ = LoadSavedAsync();
    }

    [RelayCommand]
    private async Task LoadSavedAsync()
    {
        var state = await _store.LoadAsync();
        if (state.Jobs.Count == 0)
        {
            if (Jobs.Count == 0)
                StatusMessage = "No saved jobs yet. Use Add Job or search to get started.";
            return;
        }

        Jobs = state.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobListItem(
                j.JobId,
                j.Title ?? "(No title)",
                j.Company ?? "(Unknown)",
                j.Location ?? "",
                j.IsRemote ?? false,
                j.MatchScore ?? 0.0,
                j.Source?.Url ?? "",
                j))
            .ToList()
            .AsReadOnly();
        StatusMessage = $"{state.Jobs.Count} saved job(s).";
    }

    partial void OnSelectedJobChanged(JobListItem? value)
    {
        SelectedJobTitle = value?.Title ?? "";
        SelectedJobCompany = value?.Company ?? "";
        SelectedJobLocation = value?.Location ?? "";
        SelectedJobIsRemote = value?.IsRemote ?? false;
        SelectedJobDescription = value?.FullJob.RawText ?? "";
        HasJdQualityReport = false;
        JdQualityFindings = [];
        HasCoverageReport = false;
        CoverageItems = [];
        TailorSelectedJobCommand.NotifyCanExecuteChanged();

        // Cancel previous refresh and start a fresh one
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        _ = RefreshAspectsAsync(_refreshCts.Token);

        if (value is not null)
        {
            _ = RunJdQualityAsync(value.FullJob);
            _ = RunCoverageAsync(value.FullJob);
        }
    }

    private Task RunJdQualityAsync(JobDescription job)
    {
        try
        {
            var report = _jobQualityAnalyser.Analyse(job);
            JdQualityScore = report.OverallScore;
            JdQualityFindings = report.Errors
                .Concat(report.Warnings)
                .Take(5)
                .Select(f => new QualityFindingViewModel(
                    f.Severity.ToString(),
                    f.Severity == FindingSeverity.Error ? "#F38BA8" : "#FAB387",
                    f.Code,
                    f.Message,
                    f.Section))
                .ToList()
                .AsReadOnly();
            HasJdQualityReport = true;
        }
        catch
        {
            // Non-critical — silently skip if JD quality fails
        }
        return Task.CompletedTask;
    }

    private async Task RunCoverageAsync(JobDescription job)
    {
        try
        {
            var state = await _store.LoadAsync();
            if (state.Resume is null) return;

            var report = await _coverageAnalyser.AnalyseAsync(state.Resume, job);
            CoveragePercent = report.CoveragePercent;
            CoverageItems = report.Entries
                .Where(e => e.Requirement.Priority == RequirementPriority.Required)
                .OrderByDescending(e => e.IsCovered)
                .Select(e => new CoverageItemViewModel(
                    e.Requirement.Text,
                    e.IsCovered,
                    e.Evidence,
                    e.IsCovered ? "#A6E3A1" : "#F38BA8"))
                .ToList()
                .AsReadOnly();
            HasCoverageReport = true;
        }
        catch
        {
            // Non-critical — silently skip
        }
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
