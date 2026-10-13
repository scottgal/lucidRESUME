using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Services;
using lucidRESUME.Matching;
using lucidRESUME.JobML;
using System.Text.Json;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ApplyPageViewModel : ViewModelBase
{
    private readonly SemanticCompressor _compressor;
    private readonly CareerRecordJobMlBuilder _careerRecordBuilder;
    private List<RoleInterviewTopic> _interviewTopics = [];
    private readonly HashSet<string> _handledTopics = [];
    private RoleInterviewTopic? _interviewTopic;
    private Guid? _preferredInterviewRole;
    private int _contextVersion;
    private bool _settingParsedFields;
    private readonly IInterviewAssessor _interviewAssessor;
    private CancellationTokenSource? _assessmentCancellation;
    private int _assessmentGeneration;
    private InterviewAssessment? _assessment;
    private readonly ICoverageAnalyser _coverageAnalyser;
    private readonly IJobSpecParser _jobParser;
    private readonly IAppStore _store;
    private readonly ResumeArtifactBuilder _artifactBuilder;
    private readonly GeneratedResumeArchive _archive;
    public Action? OpenArchive { get; set; }
    private readonly IReadOnlyList<IResumeExporter> _exporters;

    internal TopLevel? TopLevel { get; set; }

    private ResumeDocument? _contextResume;
    private JobDescription? _contextJob;
    private ResumeDocument? _generatedResume;
    private bool _loadingSavedJobs;
    public IReadOnlyList<ResumeLength> LengthOptions { get; } = Enum.GetValues<ResumeLength>();
    public IReadOnlyList<ResumeDetail> DetailOptions { get; } = Enum.GetValues<ResumeDetail>();
    public IReadOnlyList<ResumeTone> ToneOptions { get; } = Enum.GetValues<ResumeTone>();
    [ObservableProperty] private ResumeLength _selectedLength = ResumeLength.Automatic;
    [ObservableProperty] private ResumeDetail _selectedDetail = ResumeDetail.Balanced;
    [ObservableProperty] private ResumeTone _selectedTone = ResumeTone.Direct;
    [ObservableProperty] private IReadOnlyList<SavedJobItem> _savedJobs = [];
    [ObservableProperty] private SavedJobItem? _selectedSavedJob;

    public async Task LoadSavedJobsAsync()
    {
        var state = await _store.LoadAsync();
        _loadingSavedJobs = true;
        try
        {
            SavedJobs = state.Jobs.Where(job => !job.IsEmployerRole).OrderByDescending(job => job.CreatedAt)
                .Select(job => new SavedJobItem(job.JobId, job.Title ?? "Untitled job", job.Company ?? "", job.Location ?? "", "", job)).ToList();
            SelectedSavedJob = SavedJobs.FirstOrDefault(item => item.JobId == _contextJob?.JobId);
        }
        finally { _loadingSavedJobs = false; }
    }

    partial void OnSelectedSavedJobChanged(SavedJobItem? value)
    {
        if (!_loadingSavedJobs && value is not null) SetContext(null, value.FullJob);
    }

    [ObservableProperty] private string _jobTitle = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _jobDescriptionText = "";
    [ObservableProperty] private string? _tailoredMarkdown;
    [ObservableProperty] private bool _isTailoring;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyResult)), NotifyPropertyChangedFor(nameof(ShowProjectedResult)), NotifyPropertyChangedFor(nameof(CanImproveEvidence))] private bool _hasResult;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private bool _isTranscriptBuilder;
    [ObservableProperty] private string _interviewSkills = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanImproveEvidence))] private int _interviewRemaining;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyResult)), NotifyPropertyChangedFor(nameof(ShowProjectedResult)), NotifyPropertyChangedFor(nameof(CanImproveEvidence))] private bool _hasInterview;
    public bool ShowEmptyResult => !HasInterview && !HasResult;
    public bool ShowProjectedResult => HasResult && !HasInterview;
    public bool CanImproveEvidence => HasResult && !HasInterview && InterviewRemaining > 0;
    [ObservableProperty] private string? _evidenceWarning;
    [ObservableProperty] private string _interviewRequirement = "";
    [ObservableProperty] private string _interviewReason = "";
    [ObservableProperty] private string _interviewEvidence = "";
    [ObservableProperty] private string _interviewProbe = "";
    [ObservableProperty] private IReadOnlyList<InterviewRoleChoice> _interviewRoles = [];
    [ObservableProperty] private InterviewRoleChoice? _selectedInterviewRole;
    [ObservableProperty] private string _interviewContext = "";
    [ObservableProperty] private string _interviewAction = "";
    [ObservableProperty] private string _interviewOutcome = "";
    [ObservableProperty] private string _interviewOwnership = "";
    [ObservableProperty] private string _interviewObject = "";
    [ObservableProperty] private string _interviewDecision = "";
    [ObservableProperty] private string _interviewDeliveryStatus = "";
    [ObservableProperty] private string _interviewQualifiers = "";
    [ObservableProperty] private string _interviewProse = "";
    [ObservableProperty] private string _interviewNextQuestion = "";
    [ObservableProperty] private string _interviewFeedback = "";
    [ObservableProperty] private bool _isAssessingInterview;

    public IReadOnlyList<ResumeTemplate> Templates { get; } = ResumeTemplateCatalog.All;
    [ObservableProperty] private ResumeTemplate _selectedTemplate = ResumeTemplateCatalog.All[0];
    [ObservableProperty] private bool _includeCompactJobMl;

    // Compression stats
    [ObservableProperty] private string? _compressionStats;

    // Coverage summary
    [ObservableProperty] private bool _hasCoverage;
    [ObservableProperty] private int _coveragePercent;
    [ObservableProperty] private string _coverageLabel = "";
    [ObservableProperty] private int _gapCount;
    [ObservableProperty] private string _companyTypeLabel = "";

    public ApplyPageViewModel(SemanticCompressor compressor,
        ICoverageAnalyser coverageAnalyser, IJobSpecParser jobParser, IAppStore store,
        ResumeArtifactBuilder artifactBuilder,
        IEnumerable<IResumeExporter> exporters, CareerRecordJobMlBuilder careerRecordBuilder,
        IInterviewAssessor? interviewAssessor = null, GeneratedResumeArchive? archive = null)
    {
        _interviewAssessor = interviewAssessor ?? new InterviewAssessmentRules();
        _compressor = compressor;
        _careerRecordBuilder = careerRecordBuilder;
        _coverageAnalyser = coverageAnalyser;
        _jobParser = jobParser;
        _store = store;
        _artifactBuilder = artifactBuilder;
        _archive = archive ?? new GeneratedResumeArchive(store, exporters);
        _exporters = exporters.ToList();
    }

    /// <summary>Called by JobsPage or ResumePageViewModel to pre-populate the form.</summary>
    public void SetContext(ResumeDocument? resume, JobDescription? job)
    {
        ResetInterview();
        _contextResume = resume;
        HasResult = false;
        HasCoverage = false;
        TailoredMarkdown = null;
        _generatedResume = null;

        if (job is not null)
        {
            JobTitle = job.Title ?? "";
            Company = job.Company ?? "";
            JobDescriptionText = job.RawText;
        }

        _contextJob = job;
        if (resume is not null && job is not null)
            _ = RunCoverageAsync(resume, job);
    }

    private async Task<CoverageReport?> RunCoverageAsync(ResumeDocument resume, JobDescription job)
    {
        var version = _contextVersion;
        try
        {
            if (await _jobParser.EnsureRequirementsCurrentAsync(job))
            {
                if (version != _contextVersion) return null;
                await _store.MutateAsync(state =>
                {
                    var index = state.Jobs.FindIndex(saved => saved.JobId == job.JobId);
                    if (index >= 0 && state.Jobs[index].RawText == job.RawText) state.Jobs[index] = job;
                });
            }
            var report = await _coverageAnalyser.AnalyseAsync(resume, job);
            if (version != _contextVersion) return null;
            CoveragePercent = report.CoveragePercent;
            CoverageLabel = job.Competencies.Count > 0 ? "Competency evidence review" : $"Explicit evidence coverage: {report.CoveragePercent}%";
            GapCount = report.Entries.Count(e => e.Requirement.Priority == RequirementPriority.Required &&
                (!e.IsCovered || !e.IsDirectMatch));
            CompanyTypeLabel = report.CompanyType == CompanyType.Unknown
                ? ""
                : report.CompanyType.ToString();
            HasCoverage = true;
            return report;
        }
        catch
        {
            // Non-critical
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTailor))]
    private async Task SmartCompressAsync()
    {
        ErrorMessage = null;
        IsTailoring = true;
        HasResult = false;
        StatusMessage = "Selecting ledger evidence for this role…";
        CompressionStats = null;
        EvidenceWarning = null;
        HasInterview = false;
        CancelAssessment();
        var version = _contextVersion;

        try
        {
            var state = await _store.LoadAsync();
            if (version != _contextVersion) return;
            IsTranscriptBuilder = false;
            var resume = state.GetCompleteTranscript();
            if (resume is null) { ErrorMessage = "No resume loaded."; return; }

            var job = _contextJob ?? await _jobParser.ParseFromTextAsync(JobDescriptionText);
            if (version != _contextVersion) return;
            if (_contextJob is null)
            {
                // The explicit form values are authoritative. Parsing supplies the
                // requirement ledger, not a replacement title or company.
                _settingParsedFields = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(JobTitle)) job.Title = JobTitle;
                    else JobTitle = job.Title ?? "";
                    if (!string.IsNullOrWhiteSpace(Company)) job.Company = Company;
                    else Company = job.Company ?? "";
                    _contextJob = job;
                }
                finally { _settingParsedFields = false; }
            }

            StatusMessage = "Checking saved job requirements…";
            var coverage = await RunCoverageAsync(resume, job);
            if (version != _contextVersion) return;
            _interviewTopics = coverage is null ? [] : RoleInterviewPlanner.Plan(resume, coverage)
                .Where(topic => !_handledTopics.Contains(topic.Key)).ToList();
            InterviewRemaining = _interviewTopics.Count;
            _contextResume = resume;
            _contextJob = job;
            EvidenceWarning = coverage is null
                ? "Evidence gaps could not be assessed. Generation will use your current career evidence; review the resume before applying."
                : GapCount > 0 || InterviewRemaining > 0
                    ? "Some job requirements have limited supporting evidence. Your resume will use what is available. You can optionally add role detail and regenerate."
                    : null;

            if (job.SkillAnalysisWarnings.Count > 0)
                EvidenceWarning = string.Join(" ", job.SkillAnalysisWarnings) + " " + EvidenceWarning;
            if (version != _contextVersion) return;

            // Step 1: Semantic compression - filter to relevant evidence only
            StatusMessage = "Analysing skill coverage…";
            var progress = new Progress<string>(message =>
            {
                if (version == _contextVersion && IsTailoring) StatusMessage = message;
            });
            var compressed = await _compressor.CompressAsync(resume, job, progress: progress,
                generationOptions: new ResumeGenerationOptions { Length = SelectedLength, Detail = SelectedDetail, Tone = SelectedTone });
            await _store.MutateAsync(current =>
            {
                var saved = current.GetCompleteTranscript();
                if (saved is not null && lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(saved).SourceRevision == resume.ProseIndex.SourceRevision)
                {
                    var changed = saved.ProseIndex.AnalysedAt != resume.ProseIndex.AnalysedAt ||
                        saved.ProseIndex.AcceptedClaimsRevision != resume.ProseIndex.AcceptedClaimsRevision;
                    saved.ProseIndex = resume.ProseIndex;
                    if (changed) CareerRecordJobMlBuilder.RefreshDerivedExtensions(saved);
                }
            });

            if (version != _contextVersion) return;
            CompressionStats = $"{compressed.IncludedRoleCount}/{compressed.OriginalRoleCount} roles, " +
                $"{compressed.MatchedSkillCount}/{compressed.OriginalSkillCount} requirements, " +
                (job.Competencies.Count > 0 ? "competency evidence requires review" : $"explicit evidence coverage: {compressed.OverallFit:P0} (not eligibility)") +
                (compressed.Gaps.Count > 0 ? $", gaps: {string.Join(", ", compressed.Gaps.Take(3))}" : "");

            // Step 2: Render the selected ledger records without re-inference.
            StatusMessage = "Rendering evidence projection…";
            _generatedResume = _artifactBuilder.Build(resume, compressed.Projection, job, SelectedTemplate.Id);
            _generatedResume.IncludeCompactJobMl = IncludeCompactJobMl;
            await _archive.SaveAsync(resume, _generatedResume, job);
            if (version != _contextVersion) return;
            // Preview the human document. JobML remains embedded in the downloaded
            // artefact and is available through the evidence/editor surfaces.
            RefreshPreview();
            HasResult = true;
            StatusMessage = $"Projected {compressed.IncludedRoleCount} of {compressed.OriginalRoleCount} roles. Saved to Generated archive." +
                (_generatedResume.GenerationWarnings.Count > 0 ? " Some prose edits were unavailable or rejected; source wording was retained. Review the preview before exporting." : "");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Projection failed: {ex.Message}";
            StatusMessage = null;
        }
        finally { IsTailoring = false; }
    }

    private void ResetInterview()
    {
        _contextVersion++;
        CancelAssessment();
        _assessment = null;
        InterviewNextQuestion = InterviewFeedback = "";
        HasInterview = false;
        EvidenceWarning = null;
        InterviewRemaining = 0;
        IsTranscriptBuilder = false;
        _interviewTopics.Clear();
        _handledTopics.Clear();
        _interviewTopic = null;
        _preferredInterviewRole = null;
    }

    [RelayCommand]
    private void ImproveEvidence()
    {
        if (IsTailoring || !HasResult || _contextResume is null) return;
        ErrorMessage = null;
        ShowNextInterview();
        StatusMessage = "Optional: add any useful role detail, or return to your projected resume at any time.";
    }

    [RelayCommand]
    private void CloseInterview()
    {
        if (IsTailoring) return;
        CancelAssessment();
        HasInterview = false;
        ErrorMessage = null;
        StatusMessage = "Your projected resume is ready. Regenerate if you saved new role evidence.";
    }

    private void ShowNextInterview()
    {
        _interviewTopic = (IsTranscriptBuilder && _preferredInterviewRole.HasValue
            ? _interviewTopics.FirstOrDefault(t => t.SuggestedRoleId == _preferredInterviewRole && !_handledTopics.Contains(t.Key)) : null)
            ?? _interviewTopics.FirstOrDefault(t => !_handledTopics.Contains(t.Key));
        HasInterview = _interviewTopic is not null;
        InterviewRemaining = _interviewTopics.Count(t => !_handledTopics.Contains(t.Key));
        if (_interviewTopic is null) { CancelAssessment(); return; }
        InterviewRequirement = _interviewTopic.Requirement;
        InterviewReason = _interviewTopic.Reason;
        InterviewRemaining = _interviewTopics.Count(t => !_handledTopics.Contains(t.Key));
        InterviewProbe = _interviewTopic.Prompt ?? RoleInterviewPlanner.ProbeFor(_interviewTopic.Requirement, _contextJob?.Title);
        InterviewEvidence = _interviewTopic.ExistingEvidence ?? "No example found yet.";
        var choices = _contextResume!.Experience.Where(RoleEvidenceQuality.HasRoleIdentity).Select(r => new InterviewRoleChoice(r.Id,
            $"{r.Title} at {r.Company} ({r.StartDate?.ToString("yyyy-MM")} – {(r.IsCurrent ? "present" : r.EndDate?.ToString("yyyy-MM"))})")).ToList();
        if (!InterviewRoles.SequenceEqual(choices)) InterviewRoles = choices;
        SelectedInterviewRole = InterviewRoles.FirstOrDefault(r => r.Id == _interviewTopic.SuggestedRoleId);
        InterviewContext = InterviewAction = InterviewOutcome = InterviewProse = InterviewSkills = "";
        InterviewOwnership = InterviewObject = InterviewDecision = InterviewDeliveryStatus = InterviewQualifiers = "";
        QueueAssessment();
    }

    [RelayCommand]
    private async Task BuildCompleteTranscriptAsync()
    {
        if (IsTailoring) return;
        ErrorMessage = null;
        var version = _contextVersion;
        var state = await _store.LoadAsync();
        if (version != _contextVersion) return;
        var resume = state.GetCompleteTranscript();
        if (resume is null) { ErrorMessage = "Import a resume before building the transcript."; return; }
        ResetInterview();
        IsTranscriptBuilder = true;
        _contextResume = resume;
        _contextJob = null;
        HasResult = false;
        HasCoverage = false;
        _interviewTopics = RoleInterviewPlanner.PlanCompleteTranscript(resume).ToList();
        ShowNextInterview();
        StatusMessage = HasInterview ? "Build your complete transcript one role and topic at a time. You can stop and resume later."
            : "All role topics have reviewed accounts. You can return to a job to generate a resume.";
        var invalidRoles = resume.Experience.Count(role => !RoleEvidenceQuality.HasRoleIdentity(role));
        if (invalidRoles > 0) StatusMessage += $" {invalidRoles} imported entries need title/company review; their original data is retained.";
    }

    private InterviewAssessmentRequest AssessmentRequest() => new(SelectedInterviewRole?.Id,
        InterviewRequirement, IsTranscriptBuilder ? "" : _contextJob?.Title ?? JobTitle, InterviewEvidence,
        InterviewContext, InterviewAction, InterviewOutcome);

    private void CancelAssessment()
    {
        _assessmentGeneration++;
        _assessmentCancellation?.Cancel();
        _assessmentCancellation?.Dispose();
        _assessmentCancellation = null;
        IsAssessingInterview = false;
    }

    private void QueueAssessment()
    {
        CancelAssessment();
        _assessment = null;
        InterviewNextQuestion = InterviewFeedback = "";
        if (!HasInterview || IsTailoring) return;
        _assessmentCancellation = new CancellationTokenSource();
        _ = RunAssessmentAsync(500, _assessmentGeneration, _assessmentCancellation.Token);
    }

    private async Task RunAssessmentAsync(int delay, int generation, CancellationToken ct)
    {
        var version = _contextVersion;
        var request = AssessmentRequest();
        try
        {
            if (delay > 0) await Task.Delay(delay, ct);
            if (generation != _assessmentGeneration || version != _contextVersion) return;
            IsAssessingInterview = true;
            var result = await _interviewAssessor.AssessAsync(request, ct);
            if (ct.IsCancellationRequested || generation != _assessmentGeneration || version != _contextVersion ||
                result.InputHash != InterviewAssessmentRules.Hash(AssessmentRequest())) return;
            _assessment = result;
            InterviewNextQuestion = result.Question;
            InterviewFeedback = result.Feedback;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (generation != _assessmentGeneration || version != _contextVersion) return;
            var result = await new InterviewAssessmentRules().AssessAsync(request);
            _assessment = result;
            InterviewNextQuestion = result.Question;
            InterviewFeedback = "Assessment unavailable; showing the local follow-up.";
        }
        finally { if (generation == _assessmentGeneration) IsAssessingInterview = false; }
    }

    [RelayCommand]
    private async Task AssessInterviewAsync()
    {
        CancelAssessment();
        if (!HasInterview || IsTailoring) return;
        _assessmentCancellation = new CancellationTokenSource();
        await RunAssessmentAsync(0, _assessmentGeneration, _assessmentCancellation.Token);
    }

    partial void OnInterviewContextChanged(string value) => QueueAssessment();
    partial void OnInterviewActionChanged(string value) => QueueAssessment();
    partial void OnInterviewOutcomeChanged(string value) => QueueAssessment();
    partial void OnSelectedInterviewRoleChanged(InterviewRoleChoice? value)
    {
        if (IsTranscriptBuilder && value is not null && _interviewTopic is not null && value.Id != _interviewTopic.SuggestedRoleId)
        {
            _preferredInterviewRole = value.Id;
            ShowNextInterview();
        }
        else QueueAssessment();
    }

    [RelayCommand]
    private void DraftInterviewProse() => InterviewProse =
        RoleInterviewPlanner.Draft(InterviewContext, InterviewAction, InterviewOutcome);

    [RelayCommand]
    private async Task AcceptInterviewAsync()
    {
        if (_interviewTopic is null || _contextResume is null || IsTailoring) return;
        if (SelectedInterviewRole is null || string.IsNullOrWhiteSpace(InterviewAction) || string.IsNullOrWhiteSpace(InterviewProse))
        {
            ErrorMessage = "Select the role, describe your personal contribution, and review the prose before saving.";
            return;
        }
        if (IsTranscriptBuilder && SelectedInterviewRole.Id != _interviewTopic.SuggestedRoleId)
        {
            ErrorMessage = "Save this topic to the role it belongs to.";
            return;
        }
        ErrorMessage = null;
        IsTailoring = true;
        var version = _contextVersion;
        var topic = _interviewTopic;
        var roleId = SelectedInterviewRole.Id;
        var resumeId = _contextResume.ResumeId;
        var prose = InterviewProse.Trim();
        try
        {
            var state = await _store.LoadAsync();
            if (version != _contextVersion) return;
            var aggregateContext = _contextResume.ContentType == CompleteCareerRecord.ContentType;
            var current = state.GetCompleteTranscript()
                ?? throw new InvalidOperationException("The complete career transcript is no longer saved.");
            var original = JsonSerializer.Serialize(current);
            var updated = JsonSerializer.Deserialize<ResumeDocument>(original)!;
            var accepted = RoleInterviewRecorder.Accept(updated, roleId, new RoleInterviewEntry
            {
                TopicKey = topic.Key,
                Skills = InterviewSkills.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList(),
                Requirement = InterviewRequirement,
                Context = InterviewContext.Trim(),
                Action = InterviewAction.Trim(),
                Outcome = InterviewOutcome.Trim(),
                Account = new ContributionAccount
                {
                    Ownership = InterviewOwnership.Trim(),
                    Object = InterviewObject.Trim(),
                    Decision = InterviewDecision.Trim(),
                    DeliveryStatus = InterviewDeliveryStatus.Trim(),
                    Qualifiers = InterviewQualifiers.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
                },
                ReviewedProse = prose,
                Assessment = _assessment?.InputHash == InterviewAssessmentRules.Hash(AssessmentRequest()) ? _assessment : null
            });
            if (!accepted)
            {
                ErrorMessage = "This prose is already saved for that role. Edit it to add a distinct account, or skip this question.";
                return;
            }
            var rebuilt = await _careerRecordBuilder.BuildAsync(updated);
            var career = RoleInterviewTranscriptUpdater.Merge(current, rebuilt, roleId, prose);
            if (JobMlProcessor.Validate(career).Any(d => d.Severity == JobMlDiagnosticSeverity.Error))
                throw new InvalidOperationException("The transcript has evidence integrity errors. Review it in the JobML editor before saving.");
            updated.CanonicalMarkdown = career.Markdown;
            updated.JobMlSource = new JobMlParser().Serialize(career);
            updated.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(updated.JobMlSource);
            if (version != _contextVersion) return;
            await _store.MutateAsync(latest =>
            {
                var persisted = latest.GetCompleteTranscript();
                if (persisted is null || JsonSerializer.Serialize(persisted) != original)
                    throw new InvalidOperationException("The career record changed. Retry to save against the latest version.");
                latest.CompleteCareer!.Transcript = updated;
                latest.CompleteCareer.UpdatedAt = DateTimeOffset.UtcNow;
            });
            if (version != _contextVersion) return;
            _contextResume = aggregateContext ? (await _store.LoadAsync()).GetCompleteTranscript()! : updated;
            if (version != _contextVersion) return;
            _handledTopics.Add(topic.Key);
            if (IsTranscriptBuilder)
            {
                _interviewTopics = RoleInterviewPlanner.PlanCompleteTranscript(_contextResume).ToList();
                ShowNextInterview();
                StatusMessage = "Saved reviewed prose and named skills to the role transcript. Continue now or resume later.";
                return;
            }
            // New prose can answer other questions too. Recompute rather than asking stale gaps.
            var coverage = await _coverageAnalyser.AnalyseAsync(_contextResume, _contextJob!);
            if (version != _contextVersion) return;
            _interviewTopics = RoleInterviewPlanner.Plan(_contextResume, coverage).ToList();
            ShowNextInterview();
            await RunCoverageAsync(_contextResume, _contextJob!);
            StatusMessage = HasInterview
                ? "Saved reviewed prose to this role's complete transcript. Continue with the next question or project now."
                : "Saved reviewed prose to this role's complete transcript. Click Project Resume to generate with the new evidence.";
        }
        catch (Exception ex) { ErrorMessage = $"Could not save role evidence: {ex.Message}"; }
        finally { IsTailoring = false; }
    }

    [RelayCommand]
    private void SkipInterview()
    {
        if (_interviewTopic is null || IsTailoring) return;
        _handledTopics.Add(_interviewTopic.Key);
        ShowNextInterview();
        StatusMessage = HasInterview ? "Skipped. No evidence was added; unsupported requirements remain gaps."
            : "Questions finished. Click Project Resume to generate from the current evidence; skipped gaps remain unsupported.";
    }

    [RelayCommand]
    private async Task ProjectWithCurrentEvidenceAsync()
    {
        if (IsTailoring) return;
        HasInterview = false;
        await SmartCompressAsync();
    }

    private bool CanTailor() => !IsTailoring && !string.IsNullOrWhiteSpace(JobDescriptionText);

    partial void OnJobDescriptionTextChanged(string value)
    {
        _contextJob = null;
        ResetInterview();
        HasResult = false;
        SmartCompressCommand.NotifyCanExecuteChanged();
    }
    partial void OnJobTitleChanged(string value) { if (_settingParsedFields) return; _contextJob = null; ResetInterview(); HasResult = false; }
    partial void OnCompanyChanged(string value) { if (_settingParsedFields) return; _contextJob = null; ResetInterview(); HasResult = false; }
    partial void OnIsTailoringChanged(bool value)
    {
        if (value) CancelAssessment();
        else if (HasInterview) QueueAssessment();
        SmartCompressCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ViewArchive() => OpenArchive?.Invoke();

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task ExportAsync(string? format)
    {
        if (TopLevel is null || TailoredMarkdown is null || _generatedResume is null) return;

        var exportFormat = (format ?? "markdown").ToLowerInvariant() switch
        {
            "docx" or "word" => ExportFormat.Docx,
            "pdf" => ExportFormat.Pdf,
            _ => ExportFormat.Markdown
        };
        var extension = exportFormat switch
        {
            ExportFormat.Docx => "docx",
            ExportFormat.Pdf => "pdf",
            _ => "md"
        };
        var fileType = exportFormat switch
        {
            ExportFormat.Docx => new FilePickerFileType("Word document") { Patterns = ["*.docx"] },
            ExportFormat.Pdf => new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] },
            _ => new FilePickerFileType("Markdown + JobML") { Patterns = ["*.md"] }
        };

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Projected Resume",
            SuggestedFileName = $"projected-resume.{extension}",
            FileTypeChoices = [fileType]
        });

        if (file is null) return;

        try
        {
            _generatedResume.OutputTemplateId = SelectedTemplate.Id;
            var exporter = _exporters.FirstOrDefault(candidate => candidate.Format == exportFormat)
                           ?? throw new InvalidOperationException($"No {exportFormat} exporter is registered.");
            var bytes = await exporter.ExportAsync(_generatedResume);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
            StatusMessage = $"Exported to {file.Name}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    partial void OnHasResultChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTemplateChanged(ResumeTemplate value)
    {
        if (_generatedResume is not null) _generatedResume.OutputTemplateId = value.Id;
    }

    partial void OnIncludeCompactJobMlChanged(bool value)
    {
        if (_generatedResume is null) return;
        _generatedResume.IncludeCompactJobMl = value;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_generatedResume is null) return;
        var human = _generatedResume.CanonicalMarkdown ?? _generatedResume.RawMarkdown ?? "";
        if (!_generatedResume.IncludeCompactJobMl || string.IsNullOrWhiteSpace(_generatedResume.JobMlSource))
        {
            TailoredMarkdown = human;
            return;
        }

        var parser = new lucidRESUME.JobML.JobMlParser();
        TailoredMarkdown = parser.TryParse(_generatedResume.JobMlSource, out var full, out _)
            ? lucidRESUME.JobML.CJobMlProjector.Project(full!).Markdown
            : human;
    }
}

public sealed record InterviewRoleChoice(Guid Id, string Label)
{
    public override string ToString() => Label;
}
