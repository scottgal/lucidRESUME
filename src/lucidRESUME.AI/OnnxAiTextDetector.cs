using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// ONNX-based AI text detector using a RoBERTa model fine-tuned on ChatGPT output.
/// Returns probability 0.0-1.0 that text is AI-generated.
/// Model: onnx-community/chatgpt-detector-roberta-ONNX (INT8, ~126MB)
/// </summary>
public sealed class OnnxAiTextDetector : IDisposable
{
    private readonly InferenceSession? _session;
    private readonly BertTokenizer? _tokenizer;
    private readonly ILogger<OnnxAiTextDetector> _logger;
    private readonly int _maxSeqLen;

    public bool IsAvailable => _session is not null;

    public OnnxAiTextDetector(ILogger<OnnxAiTextDetector> logger, int maxSeqLen = 512)
    {
        _logger = logger;
        _maxSeqLen = maxSeqLen;

        var modelPath = ResolvePath("models/ai-detector/model.onnx");
        var vocabPath = ResolvePath("models/ai-detector/vocab.json");
        var mergesPath = ResolvePath("models/ai-detector/merges.txt");

        if (!File.Exists(modelPath) || !File.Exists(vocabPath) || !File.Exists(mergesPath))
        {
            _logger.LogInformation("AI text detector model not found — ONNX detection disabled. Enable in Settings to download (~126MB).");
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath, new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            });

            _tokenizer = BertTokenizer.Create(vocabPath);

            _logger.LogInformation("AI text detector loaded from {Path}", modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load AI text detector");
            _session = null;
            _tokenizer = null;
        }
    }

    /// <summary>
    /// Returns the probability (0.0-1.0) that the text is AI-generated.
    /// Processes text in chunks if longer than max sequence length, returns average.
    /// </summary>
    public float Detect(string text)
    {
        if (_session is null || _tokenizer is null || string.IsNullOrWhiteSpace(text))
            return -1f;

        try
        {
            // Split long text into chunks that fit the model's context window
            var chunks = ChunkText(text, _maxSeqLen - 10); // leave room for special tokens
            if (chunks.Count == 0) return -1f;

            var scores = new List<float>();
            foreach (var chunk in chunks)
            {
                var score = RunInference(chunk);
                if (score >= 0) scores.Add(score);
            }

            return scores.Count > 0 ? scores.Average() : -1f;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI detection inference failed");
            return -1f;
        }
    }

    private float RunInference(string text)
    {
        var encoded = _tokenizer!.EncodeToIds(text, _maxSeqLen, out _, out _);
        var inputIds = encoded.ToArray();
        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);

        var inputIdsLong = inputIds.Select(id => (long)id).ToArray();

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(inputIdsLong, [1, inputIdsLong.Length])),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(attentionMask, [1, attentionMask.Length]))
        };

        using var results = _session!.Run(inputs);
        var logits = results.First().AsTensor<float>();

        // Binary classification: [0] = Human, [1] = AI
        // Apply softmax to get probabilities
        var human = logits[0, 0];
        var ai = logits[0, 1];
        var maxVal = Math.Max(human, ai);
        var expHuman = (float)Math.Exp(human - maxVal);
        var expAi = (float)Math.Exp(ai - maxVal);
        var aiProb = expAi / (expHuman + expAi);

        return aiProb;
    }

    private List<string> ChunkText(string text, int maxTokens)
    {
        var chunks = new List<string>();
        // Simple sentence-based chunking
        var sentences = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var sentence in sentences)
        {
            var candidate = current.Length > 0 ? current + ". " + sentence.Trim() : sentence.Trim();
            // Rough token estimate: words * 1.3
            if (candidate.Split(' ').Length * 1.3 > maxTokens && current.Length > 0)
            {
                chunks.Add(current);
                current = sentence.Trim();
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 20) chunks.Add(current);

        return chunks;
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    public void Dispose() => _session?.Dispose();
}
