using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ResumePageViewModel : ViewModelBase
{
    private readonly IResumeParser _parser;
    private readonly IDocumentImageCache _imageCache;
    private readonly IAppStore _store;

    internal TopLevel? TopLevel { get; set; }

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

    public ResumePageViewModel(IResumeParser parser, IDocumentImageCache imageCache, IAppStore store)
    {
        _parser = parser;
        _imageCache = imageCache;
        _store = store;
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
        ErrorMessage = null;
        StatusMessage = $"Parsing {Path.GetFileName(path)}…";
        IsLoading = true;

        try
        {
            Resume = await _parser.ParseAsync(path);
            PopulateDisplayProperties();
            await LoadPageImageAsync(1);
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
        if (Resume?.ImageCacheKey is null) return;

        var path = _imageCache.GetCachedPagePath(Resume.ImageCacheKey, page);
        if (path is null) return;

        // Load bitmap off the UI thread
        var bitmap = await Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
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

        PageCount = Resume.PageCount;
        HasPageImages = Resume.ImageCacheKey is not null && PageCount > 0;
        HasResume = true;
    }
}

public record SkillGroup(string Category, string Skills);
