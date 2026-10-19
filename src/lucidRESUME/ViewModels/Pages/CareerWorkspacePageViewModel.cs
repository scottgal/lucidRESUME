using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class CareerWorkspacePageViewModel(
    IAppStore store, CareerRecordJobMlBuilder builder, ICoverageAnalyser coverage) : ViewModelBase
{
    private static readonly JsonSerializerOptions RevisionOptions = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers = { info =>
            {
                if (info.Type != typeof(ResumeDocument)) return;
                var derived = info.Properties.FirstOrDefault(property => property.Name == nameof(ResumeDocument.ProseIndex));
                if (derived is not null) info.Properties.Remove(derived);
            } }
        }
    };
    private static string Revision(ResumeDocument document) => JsonSerializer.Serialize(document, RevisionOptions);
    internal Avalonia.Controls.TopLevel? TopLevel { get; set; }
    private ResumeDocument? _snapshot;
    private string? _revision;
    private bool _loading;
    private int _gapVersion;
    private bool _completeChanged;
    private bool _reading;
    private string? _displayRevision;
    public Action<string>? NavigateTo { get; set; }
    public Action? ImportCareer { get; set; }
    public ObservableCollection<CareerRoleDraft> Roles { get; } = [];
    [ObservableProperty] private CareerRoleDraft? _selectedRole;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _completeProse = "";
    [ObservableProperty] private int _editorTab;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private bool _showRoleDetails;
    [ObservableProperty] private string _status = "Your career is the source for every resume. Start with an import or add a role.";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private IReadOnlyList<CareerEvidencePrompt> _evidencePrompts = [];
    [ObservableProperty] private string _ledgerSummary = "No saved career evidence yet.";
    [ObservableProperty] private IReadOnlyList<LedgerAuditFinding> _ledgerFindings = [];
    [ObservableProperty] private IReadOnlyList<SavedJobItem> _targetJobs = [];
    [ObservableProperty] private SavedJobItem? _selectedTargetJob;
    public bool HasRoles => Roles.Count > 0;
    public string SaveLabel => HasChanges ? "Save career" : "Career saved";
    public string EvidenceExample => EvidencePrompts.FirstOrDefault() is { } prompt
        ? $"Try adding: {prompt.Requirement}. {prompt.Question}"
        : "Describe your contribution, the tools you used and a concrete outcome.";
    partial void OnEvidencePromptsChanged(IReadOnlyList<CareerEvidencePrompt> value) => OnPropertyChanged(nameof(EvidenceExample));

    public async Task LoadAsync()
    {
        if (IsBusy || _reading) return;
        _reading = true;
        IsBusy = _snapshot is null && Roles.Count == 0;
        ErrorMessage = null;
        try
        {
            var state = await store.LoadAsync();
            var targetId = SelectedTargetJob?.JobId;
            _loading = true;
            TargetJobs = state.Jobs.Where(job => !job.IsEmployerRole).OrderByDescending(job => job.CreatedAt)
                .Select(job => new SavedJobItem(job.JobId, job.Title ?? "Untitled job", job.Company ?? "", job.Location ?? "", "", job)).ToList();
            SelectedTargetJob = TargetJobs.FirstOrDefault(job => job.JobId == targetId);
            _loading = false;
            if (HasChanges) { await RefreshPromptsAsync(); return; }
            var selectedId = SelectedRole?.Id;
            _snapshot = state.GetCompleteTranscript();
            _revision = _snapshot is null ? null : Revision(_snapshot);
            var displayRevision = _snapshot is null ? "empty" : EvidenceLedgerBuilder.EnsureCurrent(_snapshot).SourceRevision + "|" + _snapshot.JobMlRevision;
            if (displayRevision == _displayRevision) { await RefreshPromptsAsync(); return; }
            _displayRevision = displayRevision;
            IsBusy = true;
            _loading = true;
            Roles.Clear();
            foreach (var role in (_snapshot?.Experience ?? []).OrderByDescending(role => role.IsCurrent).ThenByDescending(role => role.StartDate))
            {
                var draft = new CareerRoleDraft(role);
                draft.PropertyChanged += (_, _) => Changed();
                Roles.Add(draft);
            }
            FullName = _snapshot?.Personal.FullName ?? "";
            Summary = _snapshot?.Personal.Summary ?? "";
            SelectedRole = Roles.FirstOrDefault(role => role.Id == selectedId) ?? Roles.FirstOrDefault();
            CompleteProse = Compose();
            _completeChanged = false;
            HasChanges = false;
            OnPropertyChanged(nameof(HasRoles));
            Status = Roles.Count == 0 ? "Import a CV or add your first role. You can write in your own words." : "Choose a role to write about. Saved changes become evidence for future resumes.";
        }
        catch (Exception ex) { ErrorMessage = "Could not load your career: " + ex.Message; }
        finally { _loading = false; IsBusy = false; _reading = false; }
        await RefreshPromptsAsync();
    }

    private void Changed()
    {
        if (_loading) return;
        HasChanges = true;
        Status = "Unsaved changes — kept while you move between screens. Save career to use them in resumes.";
    }
    partial void OnFullNameChanged(string value) => Changed();
    partial void OnSummaryChanged(string value) => Changed();
    partial void OnCompleteProseChanged(string value) { if (!_loading) { _completeChanged = true; Changed(); } }
    partial void OnHasChangesChanged(bool value) => OnPropertyChanged(nameof(SaveLabel));
    partial void OnSelectedRoleChanged(CareerRoleDraft? value)
    {
        ShowRoleDetails = value is not null && (string.IsNullOrWhiteSpace(value.Title) || string.IsNullOrWhiteSpace(value.Company));
        if (!_loading) _ = RefreshPromptsAsync();
    }
    partial void OnSelectedTargetJobChanged(SavedJobItem? value) { if (!_loading) _ = RefreshPromptsAsync(); }
    partial void OnEditorTabChanged(int value)
    {
        if (_loading) return;
        if (value == 1 && !_completeChanged)
        {
            _loading = true;
            CompleteProse = Compose();
            _loading = false;
        }
        else if (value != 1 && _completeChanged)
        {
            try { ReadCompleteProse(); }
            catch (Exception ex) { ErrorMessage = ex.Message; EditorTab = 1; }
        }
    }

    private string Compose() => "## Summary\n\n" + Summary.Trim() + "\n\n" + string.Join("\n\n", Roles.Select(role =>
        "### " + role.ProseHeading + "\n\n" + role.Prose.Trim()));

    private void ReadCompleteProse()
    {
        var headings = Regex.Matches(CompleteProse, @"(?m)^(## Summary|### .+)\s*$");
        if (headings.Count != Roles.Count + 1 || headings[0].Value.Trim() != "## Summary")
            throw new InvalidOperationException("Keep the Summary and role headings when editing complete prose. Add roles and change titles in the Role editor.");
        var contents = new List<string>();
        for (var i = 0; i < headings.Count; i++)
        {
            if (i > 0 && headings[i].Value.Trim() != "### " + Roles[i - 1].ProseHeading)
                throw new InvalidOperationException("Keep the role headings in order. Change role names and dates in the Role editor.");
            var start = headings[i].Index + headings[i].Length;
            var end = i + 1 < headings.Count ? headings[i + 1].Index : CompleteProse.Length;
            contents.Add(CompleteProse[start..end].Trim());
        }
        Summary = contents[0];
        for (var i = 0; i < Roles.Count; i++) Roles[i].Prose = contents[i + 1];
        _completeChanged = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void AddRole()
    {
        if (IsBusy) return;
        if (_completeChanged)
        {
            try { ReadCompleteProse(); }
            catch (Exception ex) { ErrorMessage = ex.Message; return; }
        }
        var role = new CareerRoleDraft(new WorkExperience());
        role.PropertyChanged += (_, _) => Changed();
        Roles.Insert(0, role);
        SelectedRole = role;
        EditorTab = 0;
        OnPropertyChanged(nameof(HasRoles));
        Changed();
    }

    [RelayCommand]
    private void Import() => ImportCareer?.Invoke();
    [RelayCommand]
    private void OpenJobs() => NavigateTo?.Invoke("Apply");
    [RelayCommand]
    private void OpenAdvanced() => NavigateTo?.Invoke("JobML");
    [RelayCommand]
    private void GeneralEvidence() => SelectedTargetJob = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !HasChanges) return;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            if (_completeChanged) ReadCompleteProse();
            if (string.IsNullOrWhiteSpace(FullName)) throw new InvalidOperationException("Enter your name before saving your career.");
            var previous = _snapshot ?? ResumeDocument.Create("Complete career transcript", CompleteCareerRecord.ContentType, 0);
            var updated = JsonSerializer.Deserialize<ResumeDocument>(JsonSerializer.Serialize(previous))!;
            updated.Personal.FullName = FullName.Trim();
            updated.Personal.Summary = Summary.Trim();
            updated.Experience = Roles.Select(role => role.ToExperience()).ToList();
            var subjects = updated.Experience.Where(role => JsonSerializer.Serialize(role) !=
                    JsonSerializer.Serialize(previous.Experience.FirstOrDefault(old => old.Id == role.Id)))
                .Select(role => $"experience:{role.Id:N}").ToHashSet(StringComparer.Ordinal);
            if (previous.Personal.FullName != updated.Personal.FullName || previous.Personal.Summary != updated.Personal.Summary)
                subjects.Add("personal");
            var ledger = EvidenceLedgerBuilder.EnsureCurrent(updated);
            foreach (var claim in ledger.Claims.Where(claim => subjects.Contains(claim.SubjectId ?? "")))
                foreach (var evidence in ledger.Evidence.Where(evidence => claim.EvidenceIds.Contains(evidence.Id) && evidence.SourceResumeId == updated.ResumeId))
                {
                    evidence.SourceName = "Author-reviewed career prose";
                    evidence.ExtractionMethod = "author-edit";
                }
            var rebuilt = await builder.BuildAsync(updated);
            var merged = CareerProseEditor.Merge(previous, rebuilt, subjects);
            if (JobMlProcessor.Validate(merged).Any(item => item.Severity == JobMlDiagnosticSeverity.Error))
                throw new InvalidOperationException("The evidence links need attention. Your writing is still here; open Advanced evidence to review them.");
            updated.JobMlSource = new JobMlParser().Serialize(merged);
            updated.CanonicalMarkdown = merged.Markdown;
            updated.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(updated.JobMlSource);
            await store.MutateAsync(state =>
            {
                var latest = state.GetCompleteTranscript();
                if ((latest is null ? null : Revision(latest)) != _revision)
                    throw new InvalidOperationException("Your saved career changed in another screen. Your draft is retained; reopen the current career before merging these changes.");
                if (previous.Personal.FullName != updated.Personal.FullName)
                    state.Overrides.PersonalInfoOverrides[nameof(PersonalInfo.FullName)] = updated.Personal.FullName ?? "";
                if (previous.Personal.Summary != updated.Personal.Summary)
                    state.Overrides.PersonalInfoOverrides[nameof(PersonalInfo.Summary)] = updated.Personal.Summary ?? "";
                foreach (var role in updated.Experience)
                {
                    var old = previous.Experience.FirstOrDefault(item => item.Id == role.Id);
                    if (old is null) continue;
                    var key = AppState.CareerAnchorRoleKey(old);
                    if (old.Title != role.Title || old.Company != role.Company || old.Location != role.Location ||
                        old.StartDate != role.StartDate || old.EndDate != role.EndDate || old.IsCurrent != role.IsCurrent)
                    {
                        var existing = state.Overrides.ExperienceOverrides.FirstOrDefault(item => item.ExperienceId == role.Id);
                        state.Overrides.ExperienceOverrides.RemoveAll(item => item.ExperienceId == role.Id);
                        state.Overrides.ExperienceOverrides.Add(new ExperienceOverride
                        {
                            ExperienceId = role.Id, MatchRoleKey = existing?.MatchRoleKey ?? key,
                            Company = role.Company, Title = role.Title, Location = role.Location,
                            StartDate = role.StartDate, EndDate = role.EndDate, IsCurrent = role.IsCurrent
                        });
                    }
                    if (role.IsCareerAnchor)
                    {
                        state.Overrides.CareerAnchorExperienceIds.Add(role.Id);
                        state.Overrides.CareerAnchorRoleKeys.Add(AppState.CareerAnchorRoleKey(role));
                    }
                    else if (old.IsCareerAnchor)
                    {
                        state.Overrides.CareerAnchorExperienceIds.Remove(role.Id);
                        state.Overrides.CareerAnchorRoleKeys.Remove(key);
                        state.Overrides.CareerAnchorRoleKeys.Remove(AppState.CareerAnchorRoleKey(role));
                    }
                }
                state.SetReviewedCareer(updated);
            });
            _snapshot = (await store.LoadAsync()).GetCompleteTranscript() ?? updated;
            _revision = Revision(_snapshot);
            HasChanges = false;
            Status = "Career saved. Your own words and named skills are available for future resumes.";
            _completeChanged = false;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
        await RefreshPromptsAsync();
    }

    public async Task RefreshPromptsAsync()
    {
        var version = ++_gapVersion;
        var roleId = SelectedRole?.Id;
        var job = SelectedTargetJob?.FullJob;
        try
        {
            var state = await store.LoadAsync();
            var career = state.GetCompleteTranscript();
            if (career is null) { EvidencePrompts = []; LedgerFindings = []; LedgerSummary = "No saved career evidence yet."; return; }
            EvidenceLedgerBuilder.EnsureCurrent(career);
            var audit = EvidenceLedgerAudit.Analyse(career);
            var topics = job is null ? RoleInterviewPlanner.PlanCompleteTranscript(career)
                : RoleInterviewPlanner.Plan(career, await coverage.AnalyseAsync(career, job));
            if (version != _gapVersion) return;
            LedgerSummary = $"{audit.AcceptedClaims} accepted career claims · {audit.UnreviewedObservations} unreviewed extraction observations kept separate · {audit.Findings.Count} source-link issues. Accepted means retained for use, not independently verified.";
            LedgerFindings = audit.Findings.Where(finding => finding.SubjectId == $"experience:{roleId:N}" || finding.SubjectId == "personal").ToList();
            EvidencePrompts = topics.Where(topic => topic.SuggestedRoleId == roleId || topic.SuggestedRoleId is null)
                .Take(4).Select(topic => new CareerEvidencePrompt(topic.Requirement, topic.Reason,
                    topic.ExistingEvidence ?? "No supporting example saved yet.",
                    topic.Prompt ?? RoleInterviewPlanner.ProbeFor(topic.Requirement, job?.Title))).ToList();
        }
        catch { if (version == _gapVersion) EvidencePrompts = [new("Evidence prompts unavailable", "You can keep writing and save your career.", "", "Describe what you personally did, the tools you used and what changed.")]; }
    }
}

public sealed record CareerEvidencePrompt(string Requirement, string Reason, string ExistingEvidence, string Question);

public sealed partial class CareerRoleDraft : ObservableObject
{
    private readonly WorkExperience _original;
    public Guid Id => _original.Id;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label)), NotifyPropertyChangedFor(nameof(ProseHeading))] private string _title;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label)), NotifyPropertyChangedFor(nameof(ProseHeading))] private string _company;
    [ObservableProperty] private string _location;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProseHeading))] private string _start;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProseHeading))] private string _end;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProseHeading))] private bool _isCurrent;
    [ObservableProperty] private bool _isCareerAnchor;
    [ObservableProperty] private string _prose;
    [ObservableProperty] private string _skillsText;
    public string Label => string.IsNullOrWhiteSpace(Title + Company) ? "New role" : $"{Title} · {Company}";
    public string ProseHeading => $"{Title} | {Company} ({Start} – {(IsCurrent ? "Present" : End)})";
    public CareerRoleDraft(WorkExperience role)
    {
        _original = role;
        _title = role.Title ?? ""; _company = role.Company ?? ""; _location = role.Location ?? "";
        _start = role.StartDate?.ToString("yyyy-MM") ?? ""; _end = role.EndDate?.ToString("yyyy-MM") ?? "";
        _isCurrent = role.IsCurrent; _isCareerAnchor = role.IsCareerAnchor;
        _prose = string.Join("\n\n", role.Achievements);
        _skillsText = string.Join(", ", role.Technologies);
    }
    public WorkExperience ToExperience()
    {
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Company))
            throw new InvalidOperationException("Each role needs a title and company. Your prose is kept until you fill these in.");
        var result = JsonSerializer.Deserialize<WorkExperience>(JsonSerializer.Serialize(_original))!;
        result.Title = Title.Trim(); result.Company = Company.Trim(); result.Location = Location.Trim();
        result.StartDate = Start == _original.StartDate?.ToString("yyyy-MM") ? _original.StartDate : ParseDate(Start);
        result.EndDate = IsCurrent ? null : End == _original.EndDate?.ToString("yyyy-MM") ? _original.EndDate : ParseDate(End);
        if (result.EndDate < result.StartDate) throw new InvalidOperationException($"Check the dates for {Label}: the end is before the start.");
        result.IsCurrent = IsCurrent; result.IsCareerAnchor = IsCareerAnchor;
        result.Achievements = Regex.Split(Prose.Trim(), @"\r?\n\s*\r?\n").Select(text => Regex.Replace(text.Trim(), @"^[-*]\s+", ""))
            .Where(text => text.Length > 0).ToList();
        result.Technologies = SkillsText.Split([',', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }
    private static DateOnly? ParseDate(string text) => string.IsNullOrWhiteSpace(text) ? null :
        DateOnly.TryParseExact(text.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date :
        throw new InvalidOperationException("Use YYYY-MM for dates, or leave an unknown date blank.");
}
