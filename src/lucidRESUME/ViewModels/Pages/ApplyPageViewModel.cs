using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ApplyPageViewModel : ViewModelBase
{
    private readonly IAiTailoringService _tailoringService;
    private readonly ICoverageAnalyser _coverageAnalyser;
    private readonly IAppStore _store;

    internal TopLevel? TopLevel { get; set; }

    private ResumeDocument? _contextResume;
    private JobDescription? _contextJob;

    [ObservableProperty] private string _jobTitle = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _jobDescriptionText = "";
    [ObservableProperty] private string? _tailoredMarkdown;
    [ObservableProperty] private bool _isTailoring;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Ollama availability
    [ObservableProperty] private bool _isOllamaUnavailable;

    // Coverage summary
    [ObservableProperty] private bool _hasCoverage;
    [ObservableProperty] private int _coveragePercent;
    [ObservableProperty] private int _gapCount;
    [ObservableProperty] private string _companyTypeLabel = "";

    public ApplyPageViewModel(IAiTailoringService tailoringService,
        ICoverageAnalyser coverageAnalyser, IAppStore store)
    {
        _tailoringService = tailoringService;
        _coverageAnalyser = coverageAnalyser;
        _store = store;
        IsOllamaUnavailable = !tailoringService.IsAvailable;
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

        IsOllamaUnavailable = !_tailoringService.IsAvailable;

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

    [RelayCommand(CanExecute = nameof(CanTailor))]
    private async Task TailorAsync()
    {
        ErrorMessage = null;
        IsTailoring = true;
        HasResult = false;
        StatusMessage = "Tailoring resume…";

        try
        {
            var state = await _store.LoadAsync();
            var resume = _contextResume ?? state.Resume;
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
            TailoredMarkdown = tailored.RawMarkdown ?? tailored.PlainText ?? "(No output)";
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

    private bool CanTailor() => !IsTailoring && !string.IsNullOrWhiteSpace(JobDescriptionText);

    partial void OnJobDescriptionTextChanged(string value) => TailorCommand.NotifyCanExecuteChanged();
    partial void OnIsTailoringChanged(bool value) => TailorCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task ExportAsync()
    {
        if (TopLevel is null || TailoredMarkdown is null) return;

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Tailored Resume",
            SuggestedFileName = "tailored-resume.md",
            FileTypeChoices =
            [
                new FilePickerFileType("Markdown") { Patterns = ["*.md"] }
            ]
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(TailoredMarkdown);
            StatusMessage = $"Exported to {file.Name}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    partial void OnHasResultChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
}
