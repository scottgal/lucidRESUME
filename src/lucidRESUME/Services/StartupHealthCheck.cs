using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Services;

/// <summary>
/// Checks what services are available on startup and reports status.
/// Downloads ONNX model if missing.
/// </summary>
public sealed class StartupHealthCheck
{
    private readonly IEmbeddingService _embedder;
    private readonly EmbeddingOptions _embeddingOpts;
    private readonly OllamaOptions _ollamaOpts;
    private readonly DoclingOptions _doclingOpts;
    private readonly IDoclingClient? _docling;
    private readonly HttpClient _http;
    private readonly ILogger<StartupHealthCheck> _logger;

    public StartupHealthCheck(
        IEmbeddingService embedder,
        IOptions<EmbeddingOptions> embeddingOpts,
        IOptions<OllamaOptions> ollamaOpts,
        IOptions<DoclingOptions> doclingOpts,
        HttpClient http,
        ILogger<StartupHealthCheck> logger,
        IDoclingClient? docling = null)
    {
        _embedder = embedder;
        _embeddingOpts = embeddingOpts.Value;
        _ollamaOpts = ollamaOpts.Value;
        _doclingOpts = doclingOpts.Value;
        _docling = docling;
        _http = http;
        _logger = logger;
    }

    public string EmbeddingProvider => _embeddingOpts.Provider;
    public bool IsOnnxEmbedding => _embeddingOpts.Provider.Equals("onnx", StringComparison.OrdinalIgnoreCase);
    public bool DoclingEnabled => _doclingOpts.Enabled;
    public string OllamaUrl => _ollamaOpts.BaseUrl;

    public bool OllamaAvailable { get; private set; }
    public bool DoclingAvailable { get; private set; }
    public bool OnnxModelReady { get; private set; }
    public string? OllamaModels { get; private set; }
    public List<string> Warnings { get; } = [];

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Check ONNX model
        if (IsOnnxEmbedding)
        {
            var modelPath = Path.IsPathRooted(_embeddingOpts.OnnxModelPath)
                ? _embeddingOpts.OnnxModelPath
                : Path.Combine(AppContext.BaseDirectory, _embeddingOpts.OnnxModelPath);

            OnnxModelReady = File.Exists(modelPath);
            if (!OnnxModelReady)
            {
                _logger.LogWarning("ONNX embedding model not found at {Path}. Downloading...", modelPath);
                try
                {
                    await DownloadOnnxModelAsync(modelPath, ct);
                    OnnxModelReady = true;
                    _logger.LogInformation("ONNX model downloaded successfully");
                }
                catch (Exception ex)
                {
                    Warnings.Add($"Failed to download ONNX model: {ex.Message}. Semantic matching will be unavailable.");
                    _logger.LogError(ex, "Failed to download ONNX embedding model");
                }

                // Also download vocab if missing
                var vocabPath = Path.IsPathRooted(_embeddingOpts.VocabPath)
                    ? _embeddingOpts.VocabPath
                    : Path.Combine(AppContext.BaseDirectory, _embeddingOpts.VocabPath);
                if (!File.Exists(vocabPath))
                {
                    try
                    {
                        await DownloadFileAsync(
                            "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt",
                            vocabPath, ct);
                    }
                    catch { /* model download failure already warned */ }
                }
            }
        }

        // Check Ollama
        try
        {
            var resp = await _http.GetAsync($"{_ollamaOpts.BaseUrl}/api/tags", ct);
            OllamaAvailable = resp.IsSuccessStatusCode;
            if (OllamaAvailable)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("Ollama available at {Url}", _ollamaOpts.BaseUrl);
            }
        }
        catch
        {
            OllamaAvailable = false;
        }

        if (!OllamaAvailable)
        {
            Warnings.Add($"Ollama not available at {_ollamaOpts.BaseUrl}. AI tailoring and LLM extraction disabled. " +
                $"To enable: install Ollama, then run 'ollama pull {_ollamaOpts.Model}' and 'ollama pull {_ollamaOpts.ExtractionModel}'");
        }

        // Check Docling
        if (_doclingOpts.Enabled && _docling is not null)
        {
            DoclingAvailable = await _docling.HealthCheckAsync(ct);
            if (!DoclingAvailable)
                Warnings.Add($"Docling enabled but not available at {_doclingOpts.EffectiveBaseUrl}. OCR for scanned PDFs disabled.");
        }

        foreach (var w in Warnings)
            _logger.LogWarning("{Warning}", w);
    }

    private async Task DownloadOnnxModelAsync(string targetPath, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await DownloadFileAsync(
            "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx",
            targetPath, ct);
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(targetPath);
        await stream.CopyToAsync(file, ct);
    }
}
