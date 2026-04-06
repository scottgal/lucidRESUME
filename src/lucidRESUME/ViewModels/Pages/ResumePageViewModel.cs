using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Collabora.DocumentOpeners;
using lucidRESUME.Collabora.Services;
using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using lucidRESUME.Core.Models.Quality;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ResumePageViewModel : ViewModelBase
{
    private readonly IResumeParser _parser;
    private readonly IDocumentImageCache _imageCache;
    private readonly IAppStore _store;
    private readonly LibreOfficeService _libreOffice;
    private readonly DocumentOpenerService _openers;
    private readonly IResumeQualityAnalyser _qualityAnalyser;
    private readonly AiDetectionScorer _aiDetectionScorer;
    private readonly DeAiRewriter _deAiRewriter;
    private readonly ResumeTranslator _translator;
    private readonly EmbeddingIndexer _embeddingIndexer;
    private readonly QualitySynthesizer _synthesizer;
    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly IResumeExporter? _jsonExporter;
    private readonly IResumeExporter? _markdownExporter;
    private string? _loadedFilePath;

    // LibreOffice-generated page image paths (fallback when Docling unavailable)
    private string[] _libreOfficePageImages = [];

    internal TopLevel? TopLevel { get; set; }

    public bool HasLibreOffice => _libreOffice.IsAvailable;
    public bool HasAnyOpener => _openers.HasAny;
    public string PrimaryOpenerName => _openers.Primary?.Name ?? "Open in…";

    // Pre-built items for MenuFlyout binding — each carries its own Command
    public IReadOnlyList<OpenerItem> OpenerItems => _openers.Available
        .Select(o => new OpenerItem(o.Name, new RelayCommand(() => o.Open(_loadedFilePath ?? ""))))
        .ToList();

    [ObservableProperty] private ResumeDocument? _resume;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Structured display
    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _contactLine = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private IReadOnlyList<ExperienceItemViewModel> _experience = [];
    [ObservableProperty] private IReadOnlyList<EducationItemViewModel> _education = [];
    [ObservableProperty] private IReadOnlyList<SkillGroup> _skillGroups = [];
    [ObservableProperty] private bool _hasResume;

    // Quality analysis — synthesized suggestions (not raw findings)
    [ObservableProperty] private int _qualityScore;
    [ObservableProperty] private bool _hasQualityReport;
    [ObservableProperty] private IReadOnlyList<QualitySuggestionViewModel> _qualitySuggestions = [];
    [ObservableProperty] private IReadOnlyList<string> _resumeStrengths = [];
    // Keep raw findings for backward compat (used in AI detection too)
    [ObservableProperty] private IReadOnlyList<QualityFindingViewModel> _qualityFindings = [];

    // AI detection
    [ObservableProperty] private int _aiScore;
    [ObservableProperty] private bool _hasAiReport;
    [ObservableProperty] private IReadOnlyList<QualityFindingViewModel> _aiFindings = [];
    [ObservableProperty] private bool _isDeAiRunning;
    [ObservableProperty] private string? _deAiStatus;
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private string _translateLanguage = "German";

    // Page image display
    [ObservableProperty] private Bitmap? _currentPageImage;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private bool _hasPageImages;

    public bool CanGoToPrevPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < PageCount;

    public ResumePageViewModel(IResumeParser parser, IDocumentImageCache imageCache, IAppStore store,
        LibreOfficeService libreOffice, DocumentOpenerService openers, IResumeQualityAnalyser qualityAnalyser,
        AiDetectionScorer aiDetectionScorer,
        DeAiRewriter deAiRewriter,
        ResumeTranslator translator,
        EmbeddingIndexer embeddingIndexer,
        QualitySynthesizer synthesizer,
        SkillLedgerBuilder ledgerBuilder,
        IEnumerable<IResumeExporter> exporters)
    {
        _parser = parser;
        _imageCache = imageCache;
        _store = store;
        _libreOffice = libreOffice;
        _openers = openers;
        _qualityAnalyser = qualityAnalyser;
        _aiDetectionScorer = aiDetectionScorer;
        _deAiRewriter = deAiRewriter;
        _translator = translator;
        _embeddingIndexer = embeddingIndexer;
        _synthesizer = synthesizer;
        _ledgerBuilder = ledgerBuilder;
        var exporterList = exporters.ToList();
        _jsonExporter = exporterList.FirstOrDefault(e => e.Format == ExportFormat.JsonResume);
        _markdownExporter = exporterList.FirstOrDefault(e => e.Format == ExportFormat.Markdown);
    }

    [RelayCommand]
    private async Task ImportResumeAsync()
    {
        if (TopLevel is null) return;

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Resume",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Resume Files") { Patterns = ["*.pdf", "*.docx"] },
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] },
                new FilePickerFileType("Word Document") { Patterns = ["*.docx"] },
            ]
        });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        _loadedFilePath = path;
        _libreOfficePageImages = [];
        ErrorMessage = null;
        StatusMessage = $"Parsing {Path.GetFileName(path)}…";
        IsLoading = true;

        try
        {
            Resume = await _parser.ParseAsync(path);
            PopulateDisplayProperties();

            // Tier 2: Docling images
            if (HasPageImages)
            {
                await LoadPageImageAsync(1);
            }
            // Tier 3: LibreOffice fallback when Docling produced no images
            else if (_libreOffice.IsAvailable)
            {
                StatusMessage = $"Generating preview via LibreOffice…";
                await GenerateLibreOfficePreviewAsync(path);
            }

            StatusMessage = $"Imported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Programmatic import for UX testing — bypasses file picker dialog.</summary>
    public async Task ImportFromPathAsync(string path)
    {
        _loadedFilePath = path;
        _libreOfficePageImages = [];
        ErrorMessage = null;
        StatusMessage = $"Parsing {Path.GetFileName(path)}…";
        IsLoading = true;

        try
        {
            Resume = await _parser.ParseAsync(path);
            PopulateDisplayProperties();

            if (!HasPageImages && _libreOffice.IsAvailable)
            {
                StatusMessage = "Generating preview…";
                await GenerateLibreOfficePreviewAsync(path);
            }

            StatusMessage = $"Imported {Path.GetFileName(path)}";

            await _store.MutateAsync(state => state.Resume = Resume);

            // Index embeddings in background (non-blocking)
            if (_store is Core.Persistence.SqliteAppStore sqlStore)
                _ = _embeddingIndexer.IndexResumeAsync(Resume, sqlStore.Vectors);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task GenerateLibreOfficePreviewAsync(string filePath)
    {
        var outputDir = Path.Combine(
            Path.GetTempPath(), "lucidRESUME-preview",
            Path.GetFileNameWithoutExtension(filePath));

        _libreOfficePageImages = await _libreOffice.ConvertToImagesAsync(filePath, outputDir);

        if (_libreOfficePageImages.Length > 0)
        {
            PageCount = _libreOfficePageImages.Length;
            HasPageImages = true;
            await LoadPageImageAsync(1);
        }
    }

    [RelayCommand]
    private void OpenWithPrimary()
    {
        if (_loadedFilePath != null)
            _openers.Primary?.Open(_loadedFilePath);
    }

    [RelayCommand(CanExecute = nameof(HasResume))]
    private async Task ExportJsonAsync()
    {
        if (TopLevel is null || Resume is null || _jsonExporter is null) return;
        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export JSON Resume",
            SuggestedFileName = $"{Resume.Personal.FullName ?? "resume"}.json",
            FileTypeChoices = [new FilePickerFileType("JSON Resume") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        try
        {
            var bytes = await _jsonExporter.ExportAsync(Resume);
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(bytes);
            StatusMessage = $"Exported to {file.Name}";
        }
        catch (Exception ex) { ErrorMessage = $"Export failed: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(HasResume))]
    private async Task ExportMarkdownAsync()
    {
        if (TopLevel is null || Resume is null || _markdownExporter is null) return;
        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Markdown Resume",
            SuggestedFileName = $"{Resume.Personal.FullName ?? "resume"}.md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
        });
        if (file is null) return;
        try
        {
            var bytes = await _markdownExporter.ExportAsync(Resume);
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(bytes);
            StatusMessage = $"Exported to {file.Name}";
        }
        catch (Exception ex) { ErrorMessage = $"Export failed: {ex.Message}"; }
    }

    partial void OnHasResumeChanged(bool value)
    {
        ExportJsonCommand.NotifyCanExecuteChanged();
        ExportMarkdownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPrevPage))]
    private async Task PrevPageAsync()
    {
        if (CurrentPage > 1)
            await LoadPageImageAsync(CurrentPage - 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        if (CurrentPage < PageCount)
            await LoadPageImageAsync(CurrentPage + 1);
    }

    private async Task LoadPageImageAsync(int page)
    {
        string? imagePath = null;

        // Tier 3: LibreOffice-generated images
        if (_libreOfficePageImages.Length > 0 && page >= 1 && page <= _libreOfficePageImages.Length)
        {
            imagePath = _libreOfficePageImages[page - 1];
        }
        // Tier 2: Docling image cache
        else if (Resume?.ImageCacheKey is not null)
        {
            imagePath = _imageCache.GetCachedPagePath(Resume.ImageCacheKey, page);
        }

        if (imagePath is null || !File.Exists(imagePath)) return;

        var bitmap = await Task.Run(() =>
        {
            using var stream = File.OpenRead(imagePath);
            return new Bitmap(stream);
        });

        CurrentPageImage?.Dispose();
        CurrentPageImage = bitmap;
        CurrentPage = page;
        OnPropertyChanged(nameof(CanGoToPrevPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    private void PopulateDisplayProperties()
    {
        if (Resume is null) return;

        var p = Resume.Personal;
        FullName = p.FullName ?? Path.GetFileNameWithoutExtension(Resume.FileName);

        var contacts = new List<string>();
        if (p.Email != null) contacts.Add(p.Email);
        if (p.Phone != null) contacts.Add(p.Phone);
        if (p.Location != null) contacts.Add(p.Location);
        if (p.LinkedInUrl != null) contacts.Add(p.LinkedInUrl);
        ContactLine = string.Join("  ·  ", contacts);

        Summary = p.Summary ?? string.Empty;

        Experience = Resume.Experience
            .Select(e => new ExperienceItemViewModel(
                e.Title ?? "",
                e.Company ?? "",
                e.Location ?? "",
                FormatDateRange(e.StartDate, e.EndDate, e.IsCurrent),
                string.Join(", ", e.Technologies),
                e.Achievements.AsReadOnly()))
            .ToList();

        Education = Resume.Education
            .Select(e => new EducationItemViewModel(
                e.Degree ?? "",
                e.Institution ?? "",
                e.FieldOfStudy ?? "",
                FormatDateRange(e.StartDate, e.EndDate, false),
                e.Highlights.AsReadOnly()))
            .ToList();

        SkillGroups = Resume.Skills
            .GroupBy(s => s.Category ?? "General")
            .Select(g => new SkillGroup(g.Key, string.Join(", ", g.Select(s => s.Name))))
            .ToList();

        // Tier 2 images come from Docling; tier 3 (LibreOffice) is resolved post-parse
        PageCount = Resume.PageCount;
        HasPageImages = Resume.ImageCacheKey is not null && PageCount > 0;
        HasResume = true;
        _ = RunQualityAnalysisAsync();
        _ = RunAiDetectionAsync();
    }

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        if (s == "" && e == "") return "";
        return e == "" ? s : s == "" ? e : $"{s} – {e}";
    }

    private async Task RunQualityAnalysisAsync()
    {
        if (Resume is null) return;
        var report = await _qualityAnalyser.AnalyseAsync(Resume);
        QualityScore = report.OverallScore;
        HasQualityReport = true;

        // Build skill ledger for richer insights
        Core.Models.Skills.SkillLedger? ledger = null;
        try { ledger = await _ledgerBuilder.BuildAsync(Resume); }
        catch { /* non-blocking */ }

        // Synthesize grouped suggestions instead of raw findings
        var synthesis = _synthesizer.Synthesize(report, ledger);
        QualitySuggestions = synthesis.Suggestions
            .Select(s => new QualitySuggestionViewModel(
                s.Category,
                s.Title,
                s.Summary,
                s.Severity switch
                {
                    SuggestionSeverity.Important => "#F38BA8",
                    SuggestionSeverity.Moderate => "#F9E2AF",
                    _ => "#A6E3A1"
                },
                s.AffectedCount))
            .ToList();
        ResumeStrengths = synthesis.Strengths;

        // Keep raw findings for backward compat (AI detection panel uses same format)
        QualityFindings = report.AllFindings
            .OrderByDescending(f => f.Severity)
            .Take(5) // only show top 5 raw findings
            .Select(f => new QualityFindingViewModel(
                f.Severity.ToString(),
                f.Severity switch {
                    FindingSeverity.Error   => "#F38BA8",
                    FindingSeverity.Warning => "#FAB387",
                    _                      => "#A6E3A1"
                },
                f.Code,
                f.Message,
                f.Section))
            .ToList();
    }

    [RelayCommand]
    private async Task DeAiRewriteAsync()
    {
        if (Resume is null) return;
        IsDeAiRunning = true;
        DeAiStatus = "Rewriting AI-sounding text...";
        try
        {
            var result = await _deAiRewriter.RewriteAsync(Resume);
            DeAiStatus = $"Rewrote {result.Rewritten}/{result.TotalBullets} bullets";

            // Save the updated resume
            await _store.MutateAsync(state => state.Resume = Resume);

            // Re-run AI detection to update the score
            await RunAiDetectionAsync();

            // Refresh display
            PopulateFromResume();
        }
        catch (Exception ex)
        {
            DeAiStatus = $"Rewrite failed: {ex.Message}";
        }
        finally
        {
            IsDeAiRunning = false;
        }
    }

    [RelayCommand]
    private async Task TranslateResumeAsync()
    {
        if (Resume is null) return;
        IsTranslating = true;
        StatusMessage = $"Translating to {TranslateLanguage}...";
        try
        {
            var result = await _translator.TranslateAsync(Resume, TranslateLanguage);
            if (result.Error is not null)
            {
                ErrorMessage = result.Error;
            }
            else if (result.TranslatedDocument is not null)
            {
                Resume = result.TranslatedDocument;
                await _store.MutateAsync(state => state.Resume = Resume);
                PopulateFromResume();
                StatusMessage = $"Translated to {TranslateLanguage}" +
                    (result.Glossary?.Count > 0 ? $" ({result.Glossary.Count} terms in glossary)" : "");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Translation failed: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
        }
    }

    private void PopulateFromResume()
    {
        if (Resume is null) return;
        Experience = Resume.Experience
            .Select(e => new ExperienceItemViewModel(
                e.Title ?? "", e.Company ?? "", e.Location ?? "",
                FormatDateRange(e.StartDate, e.EndDate, e.IsCurrent),
                string.Join(", ", e.Technologies),
                e.Achievements))
            .ToList();
    }

    private async Task RunAiDetectionAsync()
    {
        if (Resume is null) return;
        try
        {
            var report = await _aiDetectionScorer.ScoreAsync(Resume);
            AiScore = report.Score;
            HasAiReport = report.Score > 0 || report.Findings.Count > 0;
            AiFindings = report.Findings
                .OrderByDescending(f => f.SignalScore)
                .Select(f => new QualityFindingViewModel(
                    f.Signal,
                    f.SignalScore > 60 ? "#F38BA8" : f.SignalScore > 30 ? "#FAB387" : "#A6E3A1",
                    "",
                    f.Message,
                    f.Signal))
                .ToList();
        }
        catch { /* non-blocking */ }
    }
}

public record SkillGroup(string Category, string Skills);

public record ExperienceItemViewModel(
    string Title,
    string Company,
    string Location,
    string DateRange,
    string TechnologiesLine,
    IReadOnlyList<string> Achievements);

public record EducationItemViewModel(
    string Degree,
    string Institution,
    string FieldOfStudy,
    string DateRange,
    IReadOnlyList<string> Highlights);

public record OpenerItem(string Name, ICommand Command);

public record QualityFindingViewModel(string Severity, string SeverityColor, string Code, string Message, string Section);

public record QualitySuggestionViewModel(
    string Category,
    string Title,
    string Summary,
    string SeverityColor,
    int AffectedCount);
