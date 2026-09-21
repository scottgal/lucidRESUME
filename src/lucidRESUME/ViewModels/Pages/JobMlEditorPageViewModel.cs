using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Ingestion.Preview;
using lucidRESUME.JobML;
using lucidRESUME.Services;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class JobMlEditorPageViewModel : ViewModelBase
{
    private readonly JobMlParser _parser = new();
    private readonly string _draftPath;
    private readonly IAppStore _store;
    private bool _refreshing;
    private bool _syncingEditors;
    private CancellationTokenSource? _autosaveCancellation;
    private CancellationTokenSource? _previewCancellation;
    private int _previewGeneration;
    private Guid? _linkedResumeId;
    private IReadOnlyList<string> _documentPreviewPaths = [];
    private string? _documentPreviewDirectory;
    private readonly IResumeExporter _docxExporter;
    private readonly MorphDocxPreviewService _morphPreview;

    internal TopLevel? TopLevel { get; set; }
    public Func<Task>? SnapshotPublished { get; set; }

    [ObservableProperty] private string _documentText = "";
    [ObservableProperty] private string _humanMarkdown = "";
    [ObservableProperty] private string _jobMlText = "";
    [ObservableProperty] private string _previewMarkdown = "";
    [ObservableProperty] private IReadOnlyList<JobMlDiagnosticItem> _diagnostics = [];
    [ObservableProperty] private IReadOnlyList<JobMlEvidenceItem> _evidence = [];
    [ObservableProperty] private string _statusMessage = "Live evidence checking is active.";
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasLinkedResume;
    [ObservableProperty] private JobMlEvidenceItem? _selectedEvidence;
    [ObservableProperty] private int _evidenceLinkCount;
    [ObservableProperty] private int _brokenLinkCount;
    [ObservableProperty] private string _selectionSummary = "Select prose, JobML, or an evidence link to trace it.";
    [ObservableProperty] private Bitmap? _documentPreviewImage;
    [ObservableProperty] private int _documentPreviewPage = 1;
    [ObservableProperty] private int _documentPreviewPageCount;
    [ObservableProperty] private bool _hasDocumentPreview;
    [ObservableProperty] private bool _isRenderingDocument;
    [ObservableProperty] private string _documentPreviewStatus = "Word preview follows the current Markdown.";
    [ObservableProperty] private int _humanTabIndex = 1;
    [ObservableProperty] private ResumeTemplate _selectedTemplate = ResumeTemplateCatalog.All[0];

    public IReadOnlyList<ResumeTemplate> Templates { get; } = ResumeTemplateCatalog.All;
    public bool CanGoToPreviousDocumentPage => DocumentPreviewPage > 1;
    public bool CanGoToNextDocumentPage => DocumentPreviewPage < DocumentPreviewPageCount;

    public JobMlEditorPageViewModel(JobMlWorkspacePath workspacePath, IAppStore store,
        IEnumerable<IResumeExporter> exporters, MorphDocxPreviewService morphPreview)
    {
        _draftPath = workspacePath.DraftPath;
        _store = store;
        _docxExporter = exporters.First(exporter => exporter.Format == ExportFormat.Docx);
        _morphPreview = morphPreview;
        if (File.Exists(_draftPath))
        {
            SetDocument(File.ReadAllText(_draftPath));
            HumanTabIndex = 0;
            StatusMessage = "Recovered the autosaved JobML workspace.";
        }
        else
        {
            CreateNewDocument(markDirty: false);
        }
    }

    partial void OnDocumentTextChanged(string value)
    {
        if (_refreshing) return;
        IsDirty = true;
        Refresh();
        QueueAutosave(value);
    }

    partial void OnHumanMarkdownChanged(string value) => UpdateFromEditors();
    partial void OnJobMlTextChanged(string value) => UpdateFromEditors();
    partial void OnSelectedTemplateChanged(ResumeTemplate value) => QueueDocumentPreview();

    partial void OnSelectedEvidenceChanged(JobMlEvidenceItem? value)
    {
        SelectionSummary = value is null
            ? "Select prose, JobML, or an evidence link to trace it."
            : $"{value.Reference}  →  {value.ClaimId}  ·  {value.State.ToLowerInvariant()}";
    }

    [RelayCommand]
    private void NewDocument() => CreateNewDocument(markDirty: true);

    private void CreateNewDocument(bool markDirty)
    {
        var file = new JobMlFile(
            "# Your Name\n\n## Experience\n\n### Organisation {#organisation}\n\nDescribe the work in human-first prose.",
            new JobMlRoot
            {
                Document = new JobMlDocumentMetadata { Id = "your-name-resume", Language = "en-GB" }
            });
        CurrentPath = null;
        _linkedResumeId = null;
        HasLinkedResume = false;
        SetDocument(_parser.Serialize(file), markDirty);
        HumanTabIndex = 1;
        StatusMessage = "New JobML document.";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (TopLevel is null) return;
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown or JobML résumé",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Markdown / JobML") { Patterns = ["*.md", "*.jobml"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        CurrentPath = file.Path.LocalPath;
        _linkedResumeId = null;
        HasLinkedResume = false;
        HumanTabIndex = 0;
        ClearDocumentPreview();
        SetDocument(await reader.ReadToEndAsync());
        StatusMessage = $"Opened {file.Name}.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (TopLevel is null) return;
        IStorageFile? file = null;
        if (!string.IsNullOrWhiteSpace(CurrentPath))
            file = await TopLevel.StorageProvider.TryGetFileFromPathAsync(CurrentPath);
        file ??= await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save JobML résumé",
            SuggestedFileName = "resume.jobml.md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("JobML Markdown") { Patterns = ["*.md"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(DocumentText);
        await writer.FlushAsync();
        CurrentPath = file.Path.LocalPath;
        IsDirty = false;
        await SaveWorkspaceAsync(DocumentText, CancellationToken.None);
        StatusMessage = $"Saved {file.Name}.";
    }

    [RelayCommand]
    private void GenerateDraft()
    {
        var markdown = DocumentText;
        if (_parser.TryParse(DocumentText, out var existing, out _))
        {
            if (existing!.Data.Claims.Count > 0)
            {
                StatusMessage = "Draft not generated: existing claims were preserved.";
                return;
            }
            markdown = existing.Markdown;
        }

        var draft = JobMlDraftGenerator.Generate(markdown);
        SetDocument(_parser.Serialize(draft));
        StatusMessage = $"Generated {draft.Data.Claims.Count} derived claims. Review is required before they count as direct coverage.";
    }

    [RelayCommand]
    private void AcceptEvidenceChanges()
    {
        if (!_parser.TryParse(DocumentText, out var file, out var error))
        {
            StatusMessage = error ?? "The document cannot be reconciled.";
            return;
        }

        var accepted = 0;
        foreach (var claim in JobMlProcessor.Reconcile(file!))
            foreach (var resolution in claim.Evidence.Where(e => e.State == EvidenceState.Changed && e.CurrentText is not null))
            {
                if (resolution.SuggestedReference is not null)
                    resolution.Evidence.Ref = resolution.SuggestedReference;
                resolution.Evidence.Fingerprint = new JobMlFingerprint
                {
                    Text = MarkdownEvidenceIndex.Fingerprint(resolution.CurrentText!)
                };
                resolution.Evidence.Selector = new JobMlTextSelector { Exact = resolution.CurrentText! };
                resolution.Evidence.State = "valid";
                accepted++;
            }

        SetDocument(_parser.Serialize(file!));
        StatusMessage = accepted == 0 ? "No uniquely resolvable evidence changes to accept." : $"Accepted {accepted} evidence change(s).";
    }

    [RelayCommand]
    private void AcceptDraftClaims()
    {
        if (!_parser.TryParse(DocumentText, out var file, out var error))
        {
            StatusMessage = error ?? "The document cannot be reviewed.";
            return;
        }

        var claims = file!.Data.Claims.Where(c =>
            string.Equals(c.Origin, "derived", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(c.Review, "accepted", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var claim in claims) claim.Review = "accepted";
        SetDocument(_parser.Serialize(file));
        StatusMessage = claims.Count == 0 ? "No draft claims await review." : $"Explicitly accepted {claims.Count} draft claim(s).";
    }

    public bool TryLoadMarkdown(string markdown, Guid resumeId, string? templateId = null)
    {
        if (IsDirty)
        {
            StatusMessage = "The editor has unsaved work. Save it or start a new document before replacing it from My CV.";
            return false;
        }

        var file = JobMlDraftGenerator.Generate(markdown);
        CurrentPath = null;
        _linkedResumeId = resumeId;
        HasLinkedResume = true;
        HumanTabIndex = 0;
        ClearDocumentPreview();
        SelectedTemplate = ResumeTemplateCatalog.Get(templateId);
        SetDocument(_parser.Serialize(file));
        StatusMessage = "Loaded résumé prose and generated reviewable JobML draft claims.";
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousDocumentPage))]
    private void PreviousDocumentPage() => LoadDocumentPreviewPage(DocumentPreviewPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoToNextDocumentPage))]
    private void NextDocumentPage() => LoadDocumentPreviewPage(DocumentPreviewPage + 1);

    private void QueueDocumentPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _previewGeneration);
        _ = RenderDocumentPreviewAfterDelayAsync(generation, _previewCancellation.Token);
    }

    private async Task RenderDocumentPreviewAfterDelayAsync(int generation, CancellationToken cancellationToken)
    {
        string? renderDirectory = null;
        try
        {
            await Task.Delay(500, cancellationToken);
            IsRenderingDocument = true;
            DocumentPreviewStatus = $"Rendering {SelectedTemplate.Name}...";

            var markdown = HumanMarkdown;
            var documentText = DocumentText;
            var template = SelectedTemplate;
            var result = await Task.Run(async () =>
            {
                var resume = ResumeDocument.Create("live-preview.docx",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 0);
                resume.RawMarkdown = markdown;
                resume.CanonicalMarkdown = markdown;
                resume.JobMlSource = documentText;
                resume.OutputTemplateId = template.Id;
                MarkdownSectionParser.PopulateSections(resume, markdown);

                var bytes = await _docxExporter.ExportAsync(resume, cancellationToken);
                renderDirectory = Path.Combine(Path.GetTempPath(), "lucidRESUME-live-preview",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(renderDirectory);
                var docxPath = Path.Combine(renderDirectory, "resume.docx");
                await File.WriteAllBytesAsync(docxPath, bytes, cancellationToken);
                var paths = await _morphPreview.RenderToImagesAsync(docxPath,
                    Path.Combine(renderDirectory, "pages"), cancellationToken);
                return (paths, template.Name);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _previewGeneration)
                throw new OperationCanceledException(cancellationToken);

            if (result.paths.Length == 0)
            {
                DocumentPreviewStatus = "Word preview could not be rendered.";
                TryDeletePreviewDirectory(renderDirectory);
                return;
            }

            var previousDirectory = _documentPreviewDirectory;
            _documentPreviewDirectory = renderDirectory;
            renderDirectory = null;
            _documentPreviewPaths = result.paths;
            DocumentPreviewPageCount = result.paths.Length;
            HasDocumentPreview = true;
            LoadDocumentPreviewPage(1);
            DocumentPreviewStatus = $"Live {result.Name} Word preview";
            TryDeletePreviewDirectory(previousDirectory);
        }
        catch (OperationCanceledException)
        {
            TryDeletePreviewDirectory(renderDirectory);
        }
        catch (Exception ex)
        {
            TryDeletePreviewDirectory(renderDirectory);
            DocumentPreviewStatus = $"Word preview failed: {ex.Message}";
        }
        finally
        {
            IsRenderingDocument = false;
        }
    }

    private void LoadDocumentPreviewPage(int page)
    {
        if (page < 1 || page > _documentPreviewPaths.Count) return;

        using var stream = File.OpenRead(_documentPreviewPaths[page - 1]);
        var bitmap = new Bitmap(stream);
        DocumentPreviewImage?.Dispose();
        DocumentPreviewImage = bitmap;
        DocumentPreviewPage = page;
        OnPropertyChanged(nameof(CanGoToPreviousDocumentPage));
        OnPropertyChanged(nameof(CanGoToNextDocumentPage));
        PreviousDocumentPageCommand.NotifyCanExecuteChanged();
        NextDocumentPageCommand.NotifyCanExecuteChanged();
    }

    private void ClearDocumentPreview()
    {
        _documentPreviewPaths = [];
        DocumentPreviewPage = 1;
        DocumentPreviewPageCount = 0;
        HasDocumentPreview = false;
        DocumentPreviewStatus = "Preparing Word preview...";
        DocumentPreviewImage?.Dispose();
        DocumentPreviewImage = null;
        var previousDirectory = _documentPreviewDirectory;
        _documentPreviewDirectory = null;
        TryDeletePreviewDirectory(previousDirectory);
        OnPropertyChanged(nameof(CanGoToPreviousDocumentPage));
        OnPropertyChanged(nameof(CanGoToNextDocumentPage));
        PreviousDocumentPageCommand.NotifyCanExecuteChanged();
        NextDocumentPageCommand.NotifyCanExecuteChanged();
    }

    private static void TryDeletePreviewDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [RelayCommand]
    private async Task PublishReviewedSnapshotAsync()
    {
        if (_linkedResumeId is not { } resumeId)
        {
            StatusMessage = "Open this document from My CV before publishing it back.";
            return;
        }
        if (!_parser.TryParse(DocumentText, out var file, out var error))
        {
            StatusMessage = error ?? "The document cannot be published.";
            return;
        }

        var errors = JobMlProcessor.Validate(file!)
            .Where(diagnostic => diagnostic.Severity == JobMlDiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            StatusMessage = $"Publish blocked: {errors.Count} evidence integrity error(s) remain.";
            return;
        }

        var unresolvedAcceptedEvidence = JobMlProcessor.Reconcile(file!)
            .Where(claim => string.Equals(claim.Claim.Review, "accepted", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Evidence)
            .Count(evidence => evidence.State is not (EvidenceState.Valid or EvidenceState.External));
        if (unresolvedAcceptedEvidence > 0)
        {
            StatusMessage = $"Publish blocked: {unresolvedAcceptedEvidence} accepted claim evidence item(s) require reconciliation.";
            return;
        }

        var revision = MarkdownEvidenceIndex.Fingerprint(DocumentText);
        var updated = false;
        await _store.MutateAsync(state =>
        {
            var resume = state.Resumes.FirstOrDefault(candidate => candidate.ResumeId == resumeId);
            if (resume is null) return;
            resume.CanonicalMarkdown = file!.Markdown;
            resume.RawMarkdown = file.Markdown;
            resume.JobMlSource = DocumentText;
            resume.JobMlRevision = revision;
            resume.OutputTemplateId = SelectedTemplate.Id;
            resume.LastModifiedAt = DateTimeOffset.UtcNow;
            updated = true;
        });

        if (!updated)
        {
            StatusMessage = "Publish failed: the linked résumé no longer exists.";
            return;
        }

        await SaveWorkspaceAsync(DocumentText, CancellationToken.None);
        IsDirty = false;
        if (SnapshotPublished is not null)
            await SnapshotPublished();
        StatusMessage = $"Published reviewed snapshot {revision[8..]} to My CV.";
    }

    private void SetDocument(string value, bool markDirty = true)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!_parser.TryParse(normalized, out var file, out var error))
        {
            _syncingEditors = true;
            HumanMarkdown = normalized;
            JobMlText = "";
            _syncingEditors = false;
            _refreshing = true;
            DocumentText = normalized;
            _refreshing = false;
            RefreshInvalid(error ?? "Invalid JobML document.");
            IsDirty = markDirty;
            QueueDocumentPreview();
            return;
        }

        _syncingEditors = true;
        HumanMarkdown = file!.Markdown;
        JobMlText = _parser.SerializeYaml(file.Data);
        _syncingEditors = false;
        _refreshing = true;
        DocumentText = _parser.Serialize(new JobMlFile(HumanMarkdown, file.Data));
        _refreshing = false;
        IsDirty = markDirty;
        Refresh(file);
        if (markDirty) QueueAutosave(DocumentText);
        QueueDocumentPreview();
    }

    private void UpdateFromEditors()
    {
        if (_syncingEditors) return;

        var combined = ComposeRawDocument(HumanMarkdown, JobMlText);
        _refreshing = true;
        DocumentText = combined;
        _refreshing = false;
        IsDirty = true;

        if (_parser.TryParseYaml(JobMlText, out var root, out var error))
            Refresh(new JobMlFile(HumanMarkdown.TrimEnd(), root!));
        else
            RefreshInvalid(error ?? "Invalid JobML YAML.");

        QueueAutosave(combined);
        QueueDocumentPreview();
    }

    private static string ComposeRawDocument(string markdown, string yaml) =>
        $"{markdown.TrimEnd()}\n\n---\n\n```jobml\n{yaml.TrimEnd()}\n```\n";

    private void QueueAutosave(string value)
    {
        _autosaveCancellation?.Cancel();
        _autosaveCancellation?.Dispose();
        _autosaveCancellation = new CancellationTokenSource();
        _ = AutosaveAfterDelayAsync(value, _autosaveCancellation.Token);
    }

    private async Task AutosaveAfterDelayAsync(string value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await SaveWorkspaceAsync(value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer editor state superseded this one.
        }
        catch (IOException ex)
        {
            StatusMessage = $"Autosave failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Autosave failed: {ex.Message}";
        }
    }

    private async Task SaveWorkspaceAsync(string value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_draftPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_draftPath, value, cancellationToken);
    }

    private void Refresh()
    {
        if (!_parser.TryParse(DocumentText, out var file, out var error))
        {
            RefreshInvalid(error ?? "Invalid JobML document.");
            return;
        }

        Refresh(file!);
    }

    private void Refresh(JobMlFile file)
    {
        (string ClaimId, string Reference)? previousSelection = SelectedEvidence is null
            ? null
            : (SelectedEvidence.ClaimId, SelectedEvidence.Reference);

        PreviewMarkdown = file.Markdown;
        var diagnostics = JobMlProcessor.Validate(file);
        Diagnostics = diagnostics.Select(d => new JobMlDiagnosticItem(
            d.Severity.ToString(), d.Code, d.Message, d.Path ?? "")).ToList();
        var index = MarkdownEvidenceIndex.Create(file.Markdown);
        Evidence = JobMlProcessor.Reconcile(file)
            .SelectMany(claim => claim.Evidence.Select(e => new JobMlEvidenceItem(
                claim.Claim.Id,
                claim.Claim.Statement,
                e.Evidence.Ref ?? e.Evidence.Uri ?? "external",
                e.State.ToString(),
                e.CurrentText ?? "No current prose resolved.",
                e.SuggestedReference,
                ResolvePassage(index, e)?.SourceStart ?? -1,
                ResolvePassage(index, e)?.SourceLength ?? 0,
                FindJobMlStart(JobMlText, claim.Claim.Id, e.Evidence.Ref ?? e.Evidence.Uri),
                FindJobMlLength(claim.Claim.Id, e.Evidence.Ref ?? e.Evidence.Uri))))
            .ToList();
        EvidenceLinkCount = Evidence.Count;
        BrokenLinkCount = Evidence.Count(item => item.State is "Missing" or "Changed" or "Ambiguous");
        SelectedEvidence = previousSelection is null
            ? null
            : Evidence.FirstOrDefault(item => item.ClaimId == previousSelection.Value.ClaimId &&
                                               item.Reference == previousSelection.Value.Reference);
        IsValid = diagnostics.All(d => d.Severity != JobMlDiagnosticSeverity.Error);
    }

    private void RefreshInvalid(string error)
    {
        PreviewMarkdown = HumanMarkdown;
        Diagnostics = [new JobMlDiagnosticItem("Error", "JML000", error, "jobml")];
        Evidence = [];
        EvidenceLinkCount = 0;
        BrokenLinkCount = 0;
        SelectedEvidence = null;
        IsValid = false;
    }

    public JobMlEvidenceItem? FindEvidenceAtHumanPosition(int position) => Evidence.FirstOrDefault(item =>
        item.SourceStart >= 0 && position >= item.SourceStart &&
        position <= item.SourceStart + Math.Max(1, item.SourceLength));

    public JobMlEvidenceItem? FindEvidenceAtJobMlPosition(int position) => Evidence.FirstOrDefault(item =>
        item.JobMlStart >= 0 && position >= item.JobMlStart &&
        position <= item.JobMlStart + Math.Max(1, item.JobMlLength));

    private static int FindJobMlStart(string yaml, string claimId, string? reference)
    {
        var claimStart = yaml.IndexOf(claimId, StringComparison.Ordinal);
        if (claimStart < 0) return -1;
        if (string.IsNullOrWhiteSpace(reference)) return claimStart;
        var referenceStart = yaml.IndexOf(reference, claimStart, StringComparison.Ordinal);
        return referenceStart >= 0 ? referenceStart : claimStart;
    }

    private static int FindJobMlLength(string claimId, string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? claimId.Length : reference.Length;

    private static ProsePassage? ResolvePassage(MarkdownEvidenceIndex index, EvidenceResolution resolution)
    {
        var reference = resolution.SuggestedReference ?? resolution.Evidence.Ref;
        return reference is not null && index.TryGet(reference, out var passage) ? passage : null;
    }
}

public sealed record JobMlDiagnosticItem(string Severity, string Code, string Message, string Path);
public sealed record JobMlEvidenceItem(
    string ClaimId,
    string Statement,
    string Reference,
    string State,
    string CurrentText,
    string? SuggestedReference,
    int SourceStart,
    int SourceLength,
    int JobMlStart,
    int JobMlLength);
