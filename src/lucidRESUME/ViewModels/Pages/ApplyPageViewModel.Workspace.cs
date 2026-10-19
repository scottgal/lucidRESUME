using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ApplyPageViewModel
{
    private Guid? _editingJobId;
    private DateTimeOffset _editingCreatedAt;
    private readonly Dictionary<Guid, JobWorkspaceDraft> _jobDrafts = [];
    [ObservableProperty] private string _jobSourceUrl = "";
    [ObservableProperty] private bool _hasReviewedDetails;
    [ObservableProperty] private bool _isWorkingOnJob;
    [ObservableProperty] private bool _hasUnsavedJob;
    [ObservableProperty] private int _workspaceTab;
    public Action? OpenCareer { get; set; }
    public Action? OpenTracking { get; set; }
    public string JobHeading => string.IsNullOrWhiteSpace(JobTitle) ? "Add a job" : JobTitle;
    public string JobStep => HasReviewedDetails ? "2  Confirm the details, then save or create a resume" : "1  Paste the advert, then extract its details";
    public IReadOnlyList<JobRequirementReview> RequirementReview => _contextJob is null || !HasReviewedDetails ? [] :
        _contextJob.RequiredSkills.Select(label => Review(label, "Required"))
            .Concat(_contextJob.PreferredSkills.Select(label => Review(label, "Preferred"))).ToList();
    public IReadOnlyList<JobRequirementReview> ExcludedExtraction => _contextJob is null || !HasReviewedDetails ? [] :
        _contextJob.SkillDecisions.Where(decision => decision.Classification is not ("skill" or "capability"))
            .Select(decision => new JobRequirementReview(decision.Label, "Excluded · " + decision.Classification, decision.SourceText ?? "No source excerpt saved. Extract details to refresh.")).ToList();
    private JobRequirementReview Review(string label, string priority)
    {
        var competency = _contextJob!.Competencies.FirstOrDefault(item => item.Name == label);
        var decision = _contextJob.SkillDecisions.FirstOrDefault(item => item.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        return new(label, priority, competency?.Description ?? decision?.SourceText ?? "No source excerpt saved. Extract details to refresh.");
    }
    private void RefreshRequirementReview()
    {
        OnPropertyChanged(nameof(RequirementReview));
        OnPropertyChanged(nameof(ExcludedExtraction));
    }

    private sealed record JobWorkspaceDraft(string Title, string Company, string Text, string Url,
        JobDescription? Parsed, bool Reviewed, bool Unsaved);
    private void RememberJobDraft()
    {
        if (HasUnsavedJob) _jobDrafts[_editingJobId ?? Guid.Empty] = new(JobTitle, Company, JobDescriptionText,
            JobSourceUrl, _contextJob, HasReviewedDetails, true);
    }
    private void SetWorkspaceContext(JobDescription? job)
    {
        _editingJobId = job is { JobId: var id } && id != Guid.Empty ? id : null;
        _editingCreatedAt = job?.CreatedAt ?? DateTimeOffset.UtcNow;
        JobSourceUrl = job?.Source.Url ?? "";
        HasReviewedDetails = job is not null;
        HasUnsavedJob = false;
        if (_jobDrafts.TryGetValue(_editingJobId ?? Guid.Empty, out var draft))
        {
            JobTitle = draft.Title; Company = draft.Company; JobDescriptionText = draft.Text;
            JobSourceUrl = draft.Url; _contextJob = draft.Parsed;
            HasReviewedDetails = draft.Reviewed; HasUnsavedJob = draft.Unsaved;
        }
        WorkspaceTab = 0;
        OnPropertyChanged(nameof(JobHeading));
        OnPropertyChanged(nameof(JobStep));
        RefreshRequirementReview();
    }
    private void JobEdited(bool descriptionChanged = false)
    {
        if (_settingParsedFields) return;
        HasUnsavedJob = true;
        StatusMessage = "Unsaved job changes — save the job to keep them for next time.";
        if (descriptionChanged) HasReviewedDetails = false;
        OnPropertyChanged(nameof(JobHeading));
    }
    partial void OnJobSourceUrlChanged(string value) => JobEdited();
    partial void OnHasReviewedDetailsChanged(bool value) { OnPropertyChanged(nameof(JobStep)); RefreshRequirementReview(); }
    partial void OnIsWorkingOnJobChanged(bool value) => SmartCompressCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void NewJob()
    {
        if (IsWorkingOnJob || IsTailoring) return;
        SetContext(null, null);
        _loadingSavedJobs = true;
        SelectedSavedJob = null;
        _loadingSavedJobs = false;
        ErrorMessage = null;
        StatusMessage = "Paste the full advert. The original text stays with the job for future reference.";
    }

    [RelayCommand]
    private async Task ExtractJobDetailsAsync()
    {
        if (IsWorkingOnJob || IsTailoring) return;
        if (string.IsNullOrWhiteSpace(JobDescriptionText)) { ErrorMessage = "Paste the job advert first."; return; }
        ErrorMessage = null;
        IsWorkingOnJob = true;
        var text = JobDescriptionText;
        var version = _contextVersion;
        StatusMessage = "Extracting role, company and requirements…";
        try
        {
            var parsed = await _jobParser.ParseFromTextAsync(text);
            if (version != _contextVersion || text != JobDescriptionText) return;
            _settingParsedFields = true;
            try
            {
                if (string.IsNullOrWhiteSpace(JobTitle)) JobTitle = parsed.Title ?? "";
                if (string.IsNullOrWhiteSpace(Company)) Company = parsed.Company ?? "";
                _contextJob = parsed;
                HasReviewedDetails = true;
                HasUnsavedJob = true;
            }
            finally { _settingParsedFields = false; }
            OnPropertyChanged(nameof(JobHeading));
            RefreshRequirementReview();
            StatusMessage = string.IsNullOrWhiteSpace(JobTitle) || string.IsNullOrWhiteSpace(Company)
                ? "Fill in the missing role title or company below. You can correct anything the parser found."
                : "Check the role title and company. Add a LinkedIn or advert URL if you want to keep its source.";
        }
        catch (Exception ex) { ErrorMessage = "Could not extract the advert: " + ex.Message; StatusMessage = null; }
        finally { IsWorkingOnJob = false; }
    }

    [RelayCommand]
    private async Task SaveJobAsync() => await SaveCurrentJobAsync();

    private async Task<bool> SaveCurrentJobAsync()
    {
        if (IsWorkingOnJob || IsTailoring) return false;
        if (_contextJob is null) await ExtractJobDetailsAsync();
        if (_contextJob is null) return false;
        if (string.IsNullOrWhiteSpace(JobTitle) || string.IsNullOrWhiteSpace(Company))
        { ErrorMessage = "Enter the role title and company before saving."; return false; }
        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(JobSourceUrl) &&
            (!Uri.TryCreate(JobSourceUrl.Trim(), UriKind.Absolute, out uri) || uri.Scheme is not ("https" or "http")))
        { ErrorMessage = "Use a full https:// or http:// advert URL, or leave the optional URL blank."; return false; }
        IsWorkingOnJob = true;
        ErrorMessage = null;
        try
        {
            var job = System.Text.Json.JsonSerializer.Deserialize<JobDescription>(System.Text.Json.JsonSerializer.Serialize(_contextJob))!;
            job.JobId = _editingJobId ?? Guid.NewGuid();
            job.CreatedAt = _editingCreatedAt == default ? DateTimeOffset.UtcNow : _editingCreatedAt;
            job.Title = JobTitle.Trim(); job.Company = Company.Trim(); job.RawText = JobDescriptionText;
            if ((job.Source.Url ?? "").Trim() != JobSourceUrl.Trim())
                job.Source = new JobSource { Type = uri is not null && (uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase))
                    ? JobSourceType.LinkedIn : uri is null ? JobSourceType.PastedText : JobSourceType.Url, Url = uri?.ToString() };
            await _store.MutateAsync(state =>
            {
                var index = state.Jobs.FindIndex(item => item.JobId == job.JobId);
                if (index >= 0) state.Jobs[index] = job;
                else state.Jobs.Add(job);
            });
            _jobDrafts.Remove(_editingJobId ?? Guid.Empty);
            _editingJobId = job.JobId;
            _contextJob = job;
            HasUnsavedJob = false;
            await LoadSavedJobsAsync();
            StatusMessage = "Job saved. Create a resume now, or return to this job later.";
            return true;
        }
        catch (Exception ex) { ErrorMessage = "Could not save the job: " + ex.Message; return false; }
        finally { IsWorkingOnJob = false; }
    }

    [RelayCommand]
    private async Task CreateResumeAsync()
    {
        if (!await SaveCurrentJobAsync()) return;
        WorkspaceTab = 1;
        await SmartCompressAsync();
    }

    [RelayCommand]
    private void EditCareer() => OpenCareer?.Invoke();

    [RelayCommand]
    private async Task TrackJobAsync()
    {
        if (!await SaveCurrentJobAsync()) return;
        await _store.MutateAsync(state =>
        {
            if (state.Applications.All(application => application.JobId != _contextJob!.JobId))
                state.Applications.Add(JobApplication.Create(_contextJob!));
        });
        OpenTracking?.Invoke();
    }
}

public sealed record JobRequirementReview(string Label, string Priority, string SourceText);
