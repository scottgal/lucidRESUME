using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Collabora.DocumentOpeners;
using lucidRESUME.Collabora.Services;
using lucidRESUME.Core.Interfaces;
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
    [ObservableProperty] private IReadOnlyList<WorkExperience> _experience = [];
    [ObservableProperty] private IReadOnlyList<Education> _education = [];
    [ObservableProperty] private IReadOnlyList<SkillGroup> _skillGroups = [];
    [ObservableProperty] private bool _hasResume;

    // Page image display
    [ObservableProperty] private Bitmap? _currentPageImage;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private bool _hasPageImages;

    public bool CanGoToPrevPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < PageCount;

    public ResumePageViewModel(IResumeParser parser, IDocumentImageCache imageCache, IAppStore store,
        LibreOfficeService libreOffice, DocumentOpenerService openers)
    {
        _parser = parser;
        _imageCache = imageCache;
        _store = store;
        _libreOffice = libreOffice;
        _openers = openers;
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
        Experience = Resume.Experience;
        Education = Resume.Education;

        SkillGroups = Resume.Skills
            .GroupBy(s => s.Category ?? "General")
            .Select(g => new SkillGroup(g.Key, string.Join(", ", g.Select(s => s.Name))))
            .ToList();

        // Tier 2 images come from Docling; tier 3 (LibreOffice) is resolved post-parse
        PageCount = Resume.PageCount;
        HasPageImages = Resume.ImageCacheKey is not null && PageCount > 0;
        HasResume = true;
    }
}

public record SkillGroup(string Category, string Skills);

public record OpenerItem(string Name, ICommand Command);
