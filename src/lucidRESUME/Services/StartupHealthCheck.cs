using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Services;

/// <summary>
/// Checks what services are available on startup and reports status.
/// Downloads ONNX models (embedding + NER) if missing, reporting progress via <see cref="OnStatusChanged"/>.
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

    /// <summary>Fired when a status message changes during startup. (service name, message)</summary>
    public event Action<string, string>? OnStatusChanged;

    public string EmbeddingProvider => _embeddingOpts.Provider;
    public bool IsOnnxEmbedding => _embeddingOpts.Provider.Equals("onnx", StringComparison.OrdinalIgnoreCase);
    public bool DoclingEnabled => _doclingOpts.Enabled;
    public string OllamaUrl => _ollamaOpts.BaseUrl;

    public bool OllamaAvailable { get; private set; }
    public bool DoclingAvailable { get; private set; }
    public bool OnnxModelReady { get; private set; }
    public bool GeneralNerReady { get; private set; }
    public bool ResumeNerReady { get; private set; }
    public string? OllamaModels { get; private set; }
    public List<string> Warnings { get; } = [];

    // HuggingFace URLs for auto-download
    private const string EmbeddingModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string EmbeddingVocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const string GeneralNerModelUrl = "https://huggingface.co/dslim/bert-base-NER/resolve/main/onnx/model.onnx";
    private const string GeneralNerVocabUrl = "https://huggingface.co/dslim/bert-base-NER/resolve/main/vocab.txt";
    private const string ResumeNerVocabUrl = "https://huggingface.co/yashpwr/resume-ner-bert-v2/resolve/main/vocab.txt";

    public async Task RunAsync(CancellationToken ct = default)
    {
        // --- Embedding model ---
        if (IsOnnxEmbedding)
            await EnsureEmbeddingModelAsync(ct);

        // --- NER models ---
        await EnsureNerModelsAsync(ct);

        // --- Check Ollama ---
        ReportStatus("ollama", "Ollama: checking...");
        try
        {
            var resp = await _http.GetAsync($"{_ollamaOpts.BaseUrl}/api/tags", ct);
            OllamaAvailable = resp.IsSuccessStatusCode;
            if (OllamaAvailable)
                _logger.LogInformation("Ollama available at {Url}", _ollamaOpts.BaseUrl);
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

        // --- Check Docling ---
        if (_doclingOpts.Enabled && _docling is not null)
        {
            DoclingAvailable = await _docling.HealthCheckAsync(ct);
            if (!DoclingAvailable)
                Warnings.Add($"Docling enabled but not available at {_doclingOpts.EffectiveBaseUrl}. OCR for scanned PDFs disabled.");
        }

        foreach (var w in Warnings)
            _logger.LogWarning("{Warning}", w);
    }

    private async Task EnsureEmbeddingModelAsync(CancellationToken ct)
    {
        var modelPath = ResolvePath(_embeddingOpts.OnnxModelPath);
        var vocabPath = ResolvePath(_embeddingOpts.VocabPath);

        OnnxModelReady = File.Exists(modelPath);
        if (!OnnxModelReady)
        {
            _logger.LogWarning("ONNX embedding model not found at {Path}. Downloading...", modelPath);
            try
            {
                ReportStatus("embedding", "Embeddings: downloading model...");
                await DownloadFileWithProgressAsync(EmbeddingModelUrl, modelPath, "embedding", "Embeddings", ct);
                OnnxModelReady = true;
                _logger.LogInformation("ONNX embedding model downloaded successfully");
            }
            catch (Exception ex)
            {
                Warnings.Add($"Failed to download ONNX embedding model: {ex.Message}. Semantic matching will be unavailable.");
                _logger.LogError(ex, "Failed to download ONNX embedding model");
            }
        }

        if (!File.Exists(vocabPath))
        {
            try
            {
                await DownloadFileAsync(EmbeddingVocabUrl, vocabPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download embedding vocab.txt");
            }
        }
    }

    private async Task EnsureNerModelsAsync(CancellationToken ct)
    {
        // General NER (dslim/bert-base-NER) — pre-exported ONNX available on HuggingFace
        var generalModelPath = ResolvePath("models/ner/model.onnx");
        var generalVocabPath = ResolvePath("models/ner/vocab.txt");

        GeneralNerReady = File.Exists(generalModelPath) && File.Exists(generalVocabPath);
        if (!GeneralNerReady)
        {
            _logger.LogWarning("General NER model not found. Downloading dslim/bert-base-NER...");
            try
            {
                if (!File.Exists(generalModelPath))
                {
                    ReportStatus("ner", "NER: downloading bert-base-NER...");
                    await DownloadFileWithProgressAsync(GeneralNerModelUrl, generalModelPath, "ner", "NER", ct);
                }
                if (!File.Exists(generalVocabPath))
                    await DownloadFileAsync(GeneralNerVocabUrl, generalVocabPath, ct);
                GeneralNerReady = true;
                _logger.LogInformation("General NER model downloaded successfully");
            }
            catch (Exception ex)
            {
                Warnings.Add($"Failed to download general NER model: {ex.Message}. Name detection will be limited.");
                _logger.LogError(ex, "Failed to download general NER model");
            }
        }

        // Resume NER (yashpwr/resume-ner-bert-v2) — no pre-exported ONNX; needs Python optimum export
        var resumeModelPath = ResolvePath("models/resume-ner/model.onnx");
        var resumeVocabPath = ResolvePath("models/resume-ner/vocab.txt");

        ResumeNerReady = File.Exists(resumeModelPath) && File.Exists(resumeVocabPath);
        if (!ResumeNerReady)
        {
            _logger.LogWarning("Resume NER model not found. Attempting export via optimum-cli...");
            try
            {
                ReportStatus("ner", "NER: exporting resume-ner model (Python)...");
                await ExportResumeNerModelAsync(resumeModelPath, resumeVocabPath, ct);
                ResumeNerReady = File.Exists(resumeModelPath) && File.Exists(resumeVocabPath);

                if (ResumeNerReady)
                    _logger.LogInformation("Resume NER model exported successfully");
                else
                {
                    // Vocab may be downloadable even if export failed
                    if (!File.Exists(resumeVocabPath))
                        await DownloadFileAsync(ResumeNerVocabUrl, resumeVocabPath, ct);
                    Warnings.Add("Resume NER model export failed. Install Python + optimum: pip install optimum[onnxruntime] transformers torch");
                }
            }
            catch (Exception ex)
            {
                Warnings.Add($"Resume NER model unavailable: {ex.Message}. Skill/degree extraction will be limited.");
                _logger.LogError(ex, "Failed to export resume NER model");
            }
        }
    }

    private async Task ExportResumeNerModelAsync(string modelPath, string vocabPath, CancellationToken ct)
    {
        var outputDir = Path.GetDirectoryName(modelPath)!;
        Directory.CreateDirectory(outputDir);

        // Check if Python + optimum are available
        var pythonPath = FindPython();
        if (pythonPath is null)
        {
            Warnings.Add("Python not found. Resume NER model requires: pip install optimum[onnxruntime] transformers torch");
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-m optimum.exporters.onnx --model yashpwr/resume-ner-bert-v2 --task token-classification \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            Warnings.Add("Failed to start Python for resume NER model export.");
            return;
        }

        // Read output asynchronously so we don't deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            _logger.LogWarning("optimum export exited with {Code}: {Stderr}", process.ExitCode, stderr);

            // Check if optimum is not installed
            if (stderr.Contains("No module named") || stderr.Contains("ModuleNotFoundError"))
                Warnings.Add("Python optimum not installed. Run: pip install optimum[onnxruntime] transformers torch");
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = name, Arguments = "--version",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);
                if (p?.ExitCode == 0) return name;
            }
            catch { /* not found */ }
        }
        return null;
    }

    private async Task DownloadFileWithProgressAsync(string url, string targetPath, string serviceKey, string serviceLabel, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(targetPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        var lastReport = DateTimeOffset.MinValue;

        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;

            // Report progress at most every 500ms
            var now = DateTimeOffset.UtcNow;
            if (now - lastReport > TimeSpan.FromMilliseconds(500))
            {
                lastReport = now;
                var mb = downloaded / (1024.0 * 1024.0);
                var progress = totalBytes.HasValue
                    ? $"{serviceLabel}: downloading {mb:F1}/{totalBytes.Value / (1024.0 * 1024.0):F0} MB"
                    : $"{serviceLabel}: downloading {mb:F1} MB";
                ReportStatus(serviceKey, progress);
            }
        }
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

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private void ReportStatus(string service, string message) =>
        OnStatusChanged?.Invoke(service, message);
}
