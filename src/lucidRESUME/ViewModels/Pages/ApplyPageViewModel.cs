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

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ApplyPageViewModel : ViewModelBase
{
    private readonly IAiTailoringService _tailoringService;
    private readonly SemanticCompressor _compressor;
    private readonly ICoverageAnalyser _coverageAnalyser;
    private readonly IAppStore _store;
    private readonly ResumeArtifactBuilder _artifactBuilder;
    private readonly IReadOnlyList<IResumeExporter> _exporters;

    internal TopLevel? TopLevel { get; set; }

    private ResumeDocument? _contextResume;
    private JobDescription? _contextJob;
    private ResumeDocument? _generatedResume;

    [ObservableProperty] private string _jobTitle = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _jobDescriptionText = "";
    [ObservableProperty] private string? _tailoredMarkdown;
    [ObservableProperty] private bool _isTailoring;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private bool _isAiUnavailable;
    public IReadOnlyList<ResumeTemplate> Templates { get; } = ResumeTemplateCatalog.All;
    [ObservableProperty] private ResumeTemplate _selectedTemplate = ResumeTemplateCatalog.All[0];

    // Compression stats
    [ObservableProperty] private string? _compressionStats;

    // Coverage summary
    [ObservableProperty] private bool _hasCoverage;
    [ObservableProperty] private int _coveragePercent;
    [ObservableProperty] private int _gapCount;
    [ObservableProperty] private string _companyTypeLabel = "";

    public ApplyPageViewModel(IAiTailoringService tailoringService,
        SemanticCompressor compressor,
        ICoverageAnalyser coverageAnalyser, IAppStore store,
        ResumeArtifactBuilder artifactBuilder,
        IEnumerable<IResumeExporter> exporters)
    {
        _tailoringService = tailoringService;
        _compressor = compressor;
        _coverageAnalyser = coverageAnalyser;
        _store = store;
        _artifactBuilder = artifactBuilder;
        _exporters = exporters.ToList();
        // Don't check at construction - service may not have pinged Ollama yet.
        // Rechecked in SetContext() and before each tailor operation.
    }

    /// <summary>Called by JobsPage or ResumePageViewModel to pre-populate the form.</summary>
    public void SetContext(ResumeDocument? resume, JobDescription? job)
    {
        _contextResume = resume;
        _contextJob = job;

        if (job is not null)
        {
            JobTitle = job.Title ?? "";
            Company = job.Company ?? "";
            JobDescriptionText = job.RawText;
        }

        _ = RefreshProviderAvailabilityAsync();

        if (resume is not null && job is not null)
            _ = RunCoverageAsync(resume, job);
    }

    private async Task RunCoverageAsync(ResumeDocument resume, JobDescription job)
    {
        try
        {
            var report = await _coverageAnalyser.AnalyseAsync(resume, job);
            CoveragePercent = report.CoveragePercent;
            GapCount = report.RequiredGaps.Count();
            CompanyTypeLabel = report.CompanyType == CompanyType.Unknown
                ? ""
                : report.CompanyType.ToString();
            HasCoverage = true;
        }
        catch
        {
            // Non-critical
        }
    }

    private async Task RefreshProviderAvailabilityAsync()
    {
        IsAiUnavailable = !await _tailoringService.CheckAvailabilityAsync();
    }

    [RelayCommand(CanExecute = nameof(CanTailor))]
    private async Task TailorAsync()
    {
        ErrorMessage = null;
        IsTailoring = true;
        HasResult = false;
        IsAiUnavailable = !await _tailoringService.CheckAvailabilityAsync();
        StatusMessage = "Tailoring resume…";

        try
        {
            var state = await _store.LoadAsync();
            var resume = _contextResume ?? state.SelectedResume;
            if (resume is null)
            {
                ErrorMessage = "No resume loaded. Please import a resume first.";
                return;
            }

            var profile = state.Profile;

            var job = _contextJob ?? new JobDescription
            {
                JobId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                Title = JobTitle,
                Company = Company,
                RawText = JobDescriptionText
            };

            var tailored = await _tailoringService.TailorAsync(resume, job, profile);
            _generatedResume = _artifactBuilder.Build(resume, tailored, job, SelectedTemplate.Id);
            TailoredMarkdown = _generatedResume.JobMlSource ?? _generatedResume.RawMarkdown ?? "(No output)";
            HasResult = true;
            StatusMessage = "Done.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tailoring failed: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsTailoring = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTailor))]
    private async Task SmartCompressAsync()
    {
        ErrorMessage = null;
        IsTailoring = true;
        HasResult = false;
        StatusMessage = "Compressing resume to match JD…";
        CompressionStats = null;

        try
        {
            var state = await _store.LoadAsync();
            var resume = _contextResume ?? state.SelectedResume;
            if (resume is null) { ErrorMessage = "No resume loaded."; return; }

            var job = _contextJob ?? new JobDescription
            {
                JobId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
                Title = JobTitle, Company = Company, RawText = JobDescriptionText
            };

            // Step 1: Semantic compression - filter to relevant evidence only
            StatusMessage = "Analysing skill coverage…";
            var compressed = await _compressor.CompressAsync(resume, job);

            CompressionStats = $"{compressed.IncludedRoleCount}/{compressed.OriginalRoleCount} roles, " +
                $"{compressed.MatchedSkillCount}/{compressed.OriginalSkillCount} skills, " +
                $"fit: {compressed.OverallFit:P0}" +
                (compressed.Gaps.Count > 0 ? $", gaps: {string.Join(", ", compressed.Gaps.Take(3))}" : "");

            // Step 2: Send compressed resume to LLM for polishing
            StatusMessage = "Polishing with AI…";
            var profile = state.Profile;
            var compressedResume = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
            compressedResume.SetDoclingOutput(compressed.Markdown, null, null);
            foreach (var entity in resume.Entities) compressedResume.AddEntity(entity);

            var tailored = await _tailoringService.TailorAsync(compressedResume, job, profile);
            _generatedResume = _artifactBuilder.Build(resume, tailored, job, SelectedTemplate.Id);
            TailoredMarkdown = _generatedResume.JobMlSource ?? _generatedResume.RawMarkdown ?? compressed.Markdown;
            HasResult = true;
            StatusMessage = $"Done. Compressed {compressed.OriginalRoleCount} → {compressed.IncludedRoleCount} roles.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Compression failed: {ex.Message}";
            StatusMessage = null;
        }
        finally { IsTailoring = false; }
    }

    private bool CanTailor() => !IsTailoring && !string.IsNullOrWhiteSpace(JobDescriptionText);

    partial void OnJobDescriptionTextChanged(string value)
    {
        TailorCommand.NotifyCanExecuteChanged();
        SmartCompressCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsTailoringChanged(bool value)
    {
        TailorCommand.NotifyCanExecuteChanged();
        SmartCompressCommand.NotifyCanExecuteChanged();
    }

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
            Title = "Export Tailored Resume",
            SuggestedFileName = $"tailored-resume.{extension}",
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
}
