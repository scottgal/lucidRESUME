using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace lucidRESUME.Extraction.Ner;

/// <summary>
/// NER detector using an ONNX model (e.g. dslim/bert-base-NER).
/// Returns empty results when no model is configured — graceful degradation.
/// Detects: PER→PersonName, ORG→Organization, LOC→Address, MISC→Miscellaneous.
/// </summary>
public sealed class OnnxNerDetector : IEntityDetector, IDisposable
{
    public string DetectorId => "onnx_ner";
    public int Priority => 300;

    private static readonly Dictionary<string, string> NerLabelMap = new()
    {
        ["PER"] = "PersonName",
        ["ORG"] = "Organization",
        ["LOC"] = "Address",
        ["MISC"] = "Miscellaneous",
    };

    private readonly OnnxNerOptions _options;
    private readonly ILogger<OnnxNerDetector> _logger;
    private InferenceSession? _session;

    public bool IsAvailable => _session is not null;

    public OnnxNerDetector(IOptions<OnnxNerOptions> options, ILogger<OnnxNerDetector> logger)
    {
        _options = options.Value;
        _logger = logger;
        TryLoadModel();
    }

    private void TryLoadModel()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
        {
            _logger.LogInformation("OnnxNerDetector: no model path configured — NER disabled");
            return;
        }

        try
        {
            _session = new InferenceSession(_options.ModelPath);
            _logger.LogInformation("OnnxNerDetector: loaded model from {Path}", _options.ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnnxNerDetector: failed to load model from {Path}", _options.ModelPath);
        }
    }

    public Task<IReadOnlyList<ExtractedEntity>> DetectAsync(DetectionContext context, CancellationToken ct = default)
    {
        if (_session is null || string.IsNullOrWhiteSpace(context.Text))
            return Task.FromResult<IReadOnlyList<ExtractedEntity>>([]);

        // Full BERT tokenization + inference requires a vocab file — configure OnnxNerOptions.ModelPath
        // alongside a compatible tokenizer to enable. Returning empty until model is wired.
        _logger.LogDebug("OnnxNerDetector: inference not yet wired — model loaded but tokenizer needed");
        return Task.FromResult<IReadOnlyList<ExtractedEntity>>([]);
    }

    public void Dispose() => _session?.Dispose();
}
