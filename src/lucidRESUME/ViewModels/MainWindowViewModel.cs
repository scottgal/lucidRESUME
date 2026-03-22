using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Services;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _selectedNav = "Resume";

    // Service status indicators
    [ObservableProperty] private string _embeddingStatus = "...";
    [ObservableProperty] private string _ollamaStatus = "...";
    [ObservableProperty] private string _doclingStatus = "Disabled";
    [ObservableProperty] private string _storeStatus = "SQLite";
    [ObservableProperty] private string? _warningMessage;

    private readonly Dictionary<string, ViewModelBase> _pages;
    private readonly StartupHealthCheck? _healthCheck;

    public MainWindowViewModel(
        ResumePageViewModel resumePage,
        JobsPageViewModel jobsPage,
        SearchPageViewModel searchPage,
        ApplyPageViewModel applyPage,
        ProfilePageViewModel profilePage,
        StartupHealthCheck? healthCheck = null)
    {
        _pages = new Dictionary<string, ViewModelBase>(StringComparer.OrdinalIgnoreCase)
        {
            ["Resume"] = resumePage,
            ["Jobs"] = jobsPage,
            ["Search"] = searchPage,
            ["Apply"] = applyPage,
            ["Profile"] = profilePage
        };
        _currentPage = resumePage;
        _healthCheck = healthCheck;
    }

    public async Task InitAsync()
    {
        if (_healthCheck is null) return;

        await _healthCheck.RunAsync();

        EmbeddingStatus = _healthCheck.IsOnnxEmbedding
            ? (_healthCheck.OnnxModelReady ? "ONNX (local)" : "ONNX (no model)")
            : "Ollama";

        OllamaStatus = _healthCheck.OllamaAvailable ? "Connected" : "Offline";
        DoclingStatus = _healthCheck.DoclingEnabled
            ? (_healthCheck.DoclingAvailable ? "Connected" : "Offline")
            : "Disabled";

        if (_healthCheck.Warnings.Count > 0)
            WarningMessage = string.Join("\n", _healthCheck.Warnings);
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (!_pages.TryGetValue(page, out var vm)) return;
        SelectedNav = page;
        CurrentPage = vm;
    }
}
