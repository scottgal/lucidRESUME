using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Services;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _selectedNav = "Resume";

    // Service status indicators — full display strings for sidebar
    [ObservableProperty] private string _embeddingLabel = "Embeddings: checking...";
    [ObservableProperty] private string _embeddingColor = "...";
    [ObservableProperty] private string _nerLabel = "NER: checking...";
    [ObservableProperty] private string _nerColor = "...";
    [ObservableProperty] private string _ollamaLabel = "Ollama: checking...";
    [ObservableProperty] private string _ollamaColor = "...";
    [ObservableProperty] private string _doclingLabel = "Docling: disabled";
    [ObservableProperty] private string _doclingColor = "Disabled";
    [ObservableProperty] private string _storeLabel = "Store: SQLite";
    [ObservableProperty] private string? _warningMessage;

    private readonly Dictionary<string, ViewModelBase> _pages;
    private readonly StartupHealthCheck? _healthCheck;

    /// <summary>Get a page VM by key. Used by UX testing to bypass UI interactions.</summary>
    public ViewModelBase? GetPage(string key) =>
        _pages.TryGetValue(key, out var vm) ? vm : null;

    public MainWindowViewModel(
        ResumePageViewModel resumePage,
        JobsPageViewModel jobsPage,
        SearchPageViewModel searchPage,
        ApplyPageViewModel applyPage,
        PipelinePageViewModel pipelinePage,
        ProfilePageViewModel profilePage,
        StartupHealthCheck? healthCheck = null)
    {
        _pages = new Dictionary<string, ViewModelBase>(StringComparer.OrdinalIgnoreCase)
        {
            ["Resume"] = resumePage,
            ["Jobs"] = jobsPage,
            ["Search"] = searchPage,
            ["Apply"] = applyPage,
            ["Pipeline"] = pipelinePage,
            ["Profile"] = profilePage
        };
        _currentPage = resumePage;
        _healthCheck = healthCheck;
    }

    public async Task InitAsync()
    {
        if (_healthCheck is null) return;

        // Wire up live progress updates from downloads
        _healthCheck.OnStatusChanged += (service, message) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (service)
                {
                    case "embedding":
                        EmbeddingLabel = message;
                        EmbeddingColor = "...";
                        break;
                    case "ner":
                        NerLabel = message;
                        NerColor = "...";
                        break;
                    case "ollama":
                        OllamaLabel = message;
                        OllamaColor = "...";
                        break;
                }
            });
        };

        await _healthCheck.RunAsync();

        // Final status after all checks complete
        if (_healthCheck.IsOnnxEmbedding)
        {
            EmbeddingLabel = _healthCheck.OnnxModelReady ? "Embeddings: ONNX (local)" : "Embeddings: no model!";
            EmbeddingColor = _healthCheck.OnnxModelReady ? "Connected" : "Offline";
        }
        else
        {
            EmbeddingLabel = "Embeddings: Ollama";
            EmbeddingColor = _healthCheck.OllamaAvailable ? "Connected" : "Offline";
        }

        // NER status — both models must be ready for full extraction
        if (_healthCheck.GeneralNerReady && _healthCheck.ResumeNerReady)
        {
            NerLabel = "NER: ready (2 models)";
            NerColor = "Connected";
        }
        else if (_healthCheck.GeneralNerReady)
        {
            NerLabel = "NER: partial (general only)";
            NerColor = "..."; // yellow — functional but limited
        }
        else
        {
            NerLabel = "NER: no models!";
            NerColor = "Offline";
        }

        OllamaLabel = _healthCheck.OllamaAvailable
            ? $"Ollama: connected ({_healthCheck.OllamaUrl})"
            : "Ollama: offline";
        OllamaColor = _healthCheck.OllamaAvailable ? "Connected" : "Offline";

        if (_healthCheck.DoclingEnabled)
        {
            DoclingLabel = _healthCheck.DoclingAvailable ? "Docling: connected" : "Docling: offline";
            DoclingColor = _healthCheck.DoclingAvailable ? "Connected" : "Offline";
        }
        else
        {
            DoclingLabel = "OCR: not needed (direct parse)";
            DoclingColor = "Disabled";
        }

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
