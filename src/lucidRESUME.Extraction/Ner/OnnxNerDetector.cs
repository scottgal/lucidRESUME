using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace lucidRESUME.Extraction.Ner;

/// <summary>
/// Token-classification NER detector using an ONNX BERT model.
///
/// Designed for yashpwr/resume-ner-bert-v2 (25 resume-specific entity types,
/// 90.87% F1) but configurable for any HuggingFace token-classification model
/// exported with Optimum:
///   optimum-cli export onnx --model yashpwr/resume-ner-bert-v2 \
///       --task token-classification --quantize ./models/resume-ner
///
/// Gracefully degrades to empty results when no model is configured.
/// </summary>
public sealed class OnnxNerDetector : IEntityDetector, IDisposable
{
    public string DetectorId => "onnx_ner";
    public int Priority => 300;

    // Maps BIO entity type names → our internal ExtractedEntity classification
    private static readonly Dictionary<string, string> EntityTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"]               = "PersonName",
        ["Email Address"]      = "Email",
        ["Skills"]             = "NerSkill",
        ["Designation"]        = "JobTitle",
        ["Worked as"]          = "JobTitle",
        ["Companies worked at"] = "Organization",
        ["College Name"]       = "Organization",
        ["Degree"]             = "Degree",
        ["Graduation Year"]    = "Date",
        ["Years of Experience"] = "YearsExperience",
        ["Location"]           = "Address",
        ["Links"]              = "Url",
    };

    private readonly OnnxNerOptions _options;
    private readonly ILogger<OnnxNerDetector> _logger;
    private readonly string[] _labels;
    private InferenceSession? _session;
    private WordpieceTokenizer? _tokenizer;

    public bool IsAvailable => _session is not null && _tokenizer is not null;

    public OnnxNerDetector(IOptions<OnnxNerOptions> options, ILogger<OnnxNerDetector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _labels = _options.Labels ?? OnnxNerOptions.DefaultLabels;
        TryLoadModel();
    }

    private void TryLoadModel()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
        {
            _logger.LogInformation("OnnxNerDetector: no model configured — NER disabled");
            return;
        }

        var vocabPath = _options.ResolvedVocabPath;
        if (string.IsNullOrEmpty(vocabPath) || !File.Exists(vocabPath))
        {
            _logger.LogWarning("OnnxNerDetector: vocab.txt not found at {Path} — NER disabled", vocabPath);
            return;
        }

        try
        {
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Environment.ProcessorCount,
            };
            _session = new InferenceSession(_options.ModelPath, sessionOptions);
            _tokenizer = new WordpieceTokenizer(vocabPath);
            _logger.LogInformation(
                "OnnxNerDetector: loaded model from {Path} ({Labels} labels)",
                _options.ModelPath, _labels.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnnxNerDetector: failed to load model from {Path}", _options.ModelPath);
        }
    }

    public Task<IReadOnlyList<ExtractedEntity>> DetectAsync(
        DetectionContext context, CancellationToken ct = default)
    {
        if (_session is null || _tokenizer is null || string.IsNullOrWhiteSpace(context.Text))
            return Task.FromResult<IReadOnlyList<ExtractedEntity>>([]);

        ct.ThrowIfCancellationRequested();

        try
        {
            var entities = RunInference(context);
            _logger.LogDebug("OnnxNerDetector: found {Count} entities", entities.Count);
            return Task.FromResult<IReadOnlyList<ExtractedEntity>>(entities);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnnxNerDetector: inference failed");
            return Task.FromResult<IReadOnlyList<ExtractedEntity>>([]);
        }
    }

    private List<ExtractedEntity> RunInference(DetectionContext context)
    {
        var text = context.Text;
        var encoding = _tokenizer!.Encode(text, _options.MaxSequenceLength);
        int seqLen = encoding.InputIds.Length;

        // Build ONNX input tensors
        var inputIdsTensor  = new DenseTensor<long>(encoding.InputIds,  [1, seqLen]);
        var attnMaskTensor  = new DenseTensor<long>(encoding.AttentionMask, [1, seqLen]);
        var typeIdsTensor   = new DenseTensor<long>(encoding.TokenTypeIds,  [1, seqLen]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",      inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attnMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", typeIdsTensor),
        };

        using var outputs = _session!.Run(inputs);

        // logits shape: [1, seqLen, numLabels]
        var logitsTensor = outputs.First(o => o.Name == "logits").AsTensor<float>();
        int numLabels = _labels.Length;

        return DecodeEntities(text, logitsTensor, seqLen, numLabels,
            encoding.CharOffsets, encoding.RealTokenCount, context.PageNumber);
    }

    // ── BIO decoding ─────────────────────────────────────────────────────────

    private List<ExtractedEntity> DecodeEntities(
        string originalText,
        Tensor<float> logits,
        int seqLen,
        int numLabels,
        (int Start, int End)[] charOffsets,
        int realTokenCount,
        int pageNumber)
    {
        var entities = new List<ExtractedEntity>();

        string? currentType = null;
        int entityCharStart = 0;
        int entityCharEnd = 0;
        var tokenScores = new List<float>();

        void EmitCurrent()
        {
            if (currentType == null || tokenScores.Count == 0) return;
            var avgScore = tokenScores.Average();
            if (avgScore >= _options.ConfidenceThreshold && entityCharEnd > entityCharStart)
            {
                var entityText = originalText[entityCharStart..entityCharEnd].Trim();
                if (entityText.Length > 0)
                {
                    var classification = EntityTypeMap.TryGetValue(currentType, out var mapped)
                        ? mapped : currentType;
                    entities.Add(ExtractedEntity.Create(
                        entityText, classification, DetectionSource.Ner, avgScore, pageNumber));
                }
            }
            currentType = null;
            tokenScores.Clear();
        }

        // Skip [CLS] (pos 0) and [SEP] (pos realTokenCount-1) and padding
        for (int pos = 1; pos < realTokenCount - 1; pos++)
        {
            var (charStart, charEnd) = charOffsets[pos];
            if (charStart < 0) continue; // special token

            // Argmax + softmax confidence
            float maxLogit = float.MinValue;
            int maxIdx = 0;
            for (int j = 0; j < numLabels; j++)
            {
                float l = logits[0, pos, j];
                if (l > maxLogit) { maxLogit = l; maxIdx = j; }
            }
            float confidence = Softmax(logits, pos, numLabels, maxIdx);

            var label = maxIdx < _labels.Length ? _labels[maxIdx] : "O";

            if (label.StartsWith("B-", StringComparison.Ordinal))
            {
                EmitCurrent();
                currentType = label[2..];
                entityCharStart = charStart;
                entityCharEnd = charEnd;
                tokenScores.Add(confidence);
            }
            else if (label.StartsWith("I-", StringComparison.Ordinal))
            {
                var iType = label[2..];
                if (iType.Equals(currentType, StringComparison.OrdinalIgnoreCase))
                {
                    entityCharEnd = charEnd;
                    tokenScores.Add(confidence);
                }
                else
                {
                    // Type mismatch — treat as B-
                    EmitCurrent();
                    currentType = iType;
                    entityCharStart = charStart;
                    entityCharEnd = charEnd;
                    tokenScores.Add(confidence);
                }
            }
            else // O
            {
                EmitCurrent();
            }
        }

        EmitCurrent();
        return entities;
    }

    private static float Softmax(Tensor<float> logits, int pos, int numLabels, int targetIdx)
    {
        // Numerically stable softmax for a single position
        float max = float.MinValue;
        for (int j = 0; j < numLabels; j++)
        {
            float v = logits[0, pos, j];
            if (v > max) max = v;
        }
        float sumExp = 0f;
        for (int j = 0; j < numLabels; j++)
            sumExp += MathF.Exp(logits[0, pos, j] - max);

        return MathF.Exp(logits[0, pos, targetIdx] - max) / sumExp;
    }

    public void Dispose() => _session?.Dispose();
}
