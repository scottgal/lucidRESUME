using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Ingestion.Preview;
using lucidRESUME.Collabora.Services;
using lucidRESUME.JobML;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class GeneratedResumesPageViewModel(IAppStore store, GeneratedResumeArchive archive,
    MorphDocxPreviewService wordPreview, LibreOfficeService libreOffice) : ViewModelBase
{
    private int _version;
    private string[] _pages = [];
    internal TopLevel? TopLevel { get; set; }
    [ObservableProperty] private IReadOnlyList<GeneratedResume> _entries = [];
    [ObservableProperty] private GeneratedResume? _selectedEntry;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Generate a resume to save its first version here.";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _previewFormat = "PDF";
    [ObservableProperty] private Bitmap? _currentPageImage;
    [ObservableProperty] private double _previewWidth = 380;
    [ObservableProperty] private int _pageNumber;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private string _markdown = "";
    [ObservableProperty] private string _warnings = "";
    [ObservableProperty] private IReadOnlyList<ArchivedEvidence> _evidence = [];
    [ObservableProperty] private ArchivedEvidence? _selectedEvidence;
    [ObservableProperty] private string _evidenceMarkdown = "Select a numbered citation to inspect its saved source evidence.";
    public bool HasSelection => SelectedEntry is not null;
    public bool HasImage => CurrentPageImage is not null;
    public Action? OpenJobs { get; set; }
    [RelayCommand]
    private void CreateNew() => OpenJobs?.Invoke();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        ErrorMessage = null;
        try
        {
            var previous = SelectedEntry?.Id;
            var loaded = (await store.LoadAsync()).GeneratedResumes.OrderByDescending(item => item.CreatedAt).ToList();
            // Archived versions are immutable. Preserve objects, image and selection on a no-op refresh.
            if (Entries.Select(item => item.Id).SequenceEqual(loaded.Select(item => item.Id))) return;
            Entries = loaded.Select(item => Entries.FirstOrDefault(existing => existing.Id == item.Id) ?? item).ToList();
            SelectedEntry = Entries.FirstOrDefault(item => item.Id == previous) ?? Entries.FirstOrDefault();
            Status = $"{Entries.Count} saved version(s). Each retains its original career evidence.";
        }
        catch (Exception ex) { ErrorMessage = $"Cannot load archive: {ex.Message}"; }
    }

    partial void OnSelectedEntryChanged(GeneratedResume? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = LoadSelectionAsync();
    }

    private async Task LoadSelectionAsync()
    {
        var version = ++_version;
        var entry = SelectedEntry;
        ErrorMessage = null;
        IsLoading = true;
        CurrentPageImage?.Dispose();
        CurrentPageImage = null;
        PageNumber = PageCount = 0;
        _pages = [];
        Markdown = Warnings = "";
        Evidence = [];
        SelectedEvidence = null;
        EvidenceMarkdown = "Select a numbered citation to inspect its saved source evidence.";
        try
        {
            if (entry is null) return;
            var parser = new JobMlParser();
            Markdown = parser.TryParse(entry.Resume.JobMlSource ?? "", out var file, out _)
                ? CJobMlProjector.Project(file!).Markdown : entry.Resume.CanonicalMarkdown ?? entry.Resume.RawMarkdown ?? "";
            Warnings = string.Join("\n", entry.Resume.GenerationWarnings);
            Evidence = ArchivedEvidenceResolver.Resolve(entry);
            await archive.EnsureFilesAsync(entry);
            if (version != _version) return;
            string[] pages;
            if (PreviewFormat == "Word")
            {
                var directory = Path.Combine(archive.DirectoryFor(entry.Id), "word-preview");
                pages = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.png").Order(StringComparer.Ordinal).ToArray() : [];
                if (pages.Length == 0)
                    pages = await Task.Run(() => wordPreview.RenderToImagesAsync(archive.FileFor(entry.Id, "docx"), directory));
                if (pages.Length == 0 && libreOffice.IsAvailable)
                    pages = await libreOffice.ConvertToImagesAsync(archive.FileFor(entry.Id, "docx"), directory);
            }
            else pages = archive.PdfPages(entry.Id);
            if (version != _version) return;
            _pages = pages;
            PageCount = pages.Length;
            if (pages.Length > 0) ShowPage(1);
            Status = pages.Length > 0 ? $"{PreviewFormat} · {entry.Label} · {pages.Length} page(s)"
                : "Word rendering is unavailable on this installation. The saved Word file and PDF preview remain available.";
        }
        catch (Exception ex) { if (version == _version) ErrorMessage = $"Cannot preview saved resume: {ex.Message}"; }
        finally
        {
            if (version == _version) { IsLoading = false; OnPropertyChanged(nameof(HasImage)); }
        }
    }

    [RelayCommand]
    private async Task SetFormatAsync(string format)
    {
        var next = format == "Word" ? "Word" : "PDF";
        if (PreviewFormat == next && HasImage) return;
        PreviewFormat = next;
        await LoadSelectionAsync();
    }

    private void ShowPage(int number)
    {
        if (number < 1 || number > _pages.Length) return;
        var image = new Bitmap(_pages[number - 1]);
        var previous = CurrentPageImage;
        CurrentPageImage = image;
        previous?.Dispose();
        PageNumber = number;
        OnPropertyChanged(nameof(HasImage));
    }
    [RelayCommand] private void ZoomIn() => PreviewWidth = Math.Min(1600, PreviewWidth + 150);
    [RelayCommand] private void ZoomOut() => PreviewWidth = Math.Max(200, PreviewWidth - 150);
    [RelayCommand] private void NextPage() => ShowPage(PageNumber + 1);
    [RelayCommand] private void PreviousPage() => ShowPage(PageNumber - 1);
    partial void OnSelectedEvidenceChanged(ArchivedEvidence? value) => EvidenceMarkdown = value?.Markdown ?? "Select a numbered citation.";
    [RelayCommand]
    private void FollowCitation(string? target)
    {
        if (target?.StartsWith("#ref-", StringComparison.Ordinal) == true && int.TryParse(target[5..], out var number))
            SelectedEvidence = Evidence.FirstOrDefault(item => item.Number == number);
    }

    [RelayCommand]
    private async Task ExportAsync(string format)
    {
        if (TopLevel is null || SelectedEntry is null) return;
        var entry = SelectedEntry;
        var extension = format switch { "docx" => "docx", "pdf" => "pdf", _ => "jobml.md" };
        try
        {
            var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "Save archived resume", SuggestedFileName = $"resume-{entry.CreatedAt:yyyyMMdd-HHmm}.{extension}" });
            if (file is null) return;
            await archive.EnsureFilesAsync(entry);
            await using var destination = await file.OpenWriteAsync();
            destination.SetLength(0);
            await using var source = File.OpenRead(archive.FileFor(entry.Id, extension));
            await source.CopyToAsync(destination);
            Status = $"Saved {file.Name}";
        }
        catch (Exception ex) { ErrorMessage = $"Cannot export saved version: {ex.Message}"; }
    }
}
