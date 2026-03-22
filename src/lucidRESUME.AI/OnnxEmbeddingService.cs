using System.Collections.Concurrent;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace lucidRESUME.AI;

/// <summary>
/// Fully local embedding service using all-MiniLM-L6-v2 ONNX model (384 dimensions).
/// No external services required — runs on CPU via ONNX Runtime.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly ConcurrentDictionary<string, float[]> _cache = new();
    private const int MaxCacheEntries = 500;
    private const int MaxSequenceLength = 256;

    public OnnxEmbeddingService(IOptions<EmbeddingOptions> options, ILogger<OnnxEmbeddingService> logger)
    {
        _logger = logger;
        var opts = options.Value;

        var modelPath = ResolvePath(opts.OnnxModelPath);
        var vocabPath = ResolvePath(opts.VocabPath);

        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions { LowerCaseBeforeTokenization = true });

        _logger.LogInformation("ONNX embedding model loaded from {Path} (384-dim)", modelPath);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(text, out var cached))
            return Task.FromResult(cached);

        var result = Embed(text);

        // Evict ~10% when cache full
        if (_cache.Count >= MaxCacheEntries)
        {
            var keys = _cache.Keys.Take(MaxCacheEntries / 10).ToList();
            foreach (var k in keys) _cache.TryRemove(k, out _);
        }
        _cache[text] = result;

        return Task.FromResult(result);
    }

    private float[] Embed(string text)
    {
        // Tokenize with special tokens [CLS] ... [SEP]
        var ids = _tokenizer.EncodeToIds(text, addSpecialTokens: true);
        var len = Math.Min(ids.Count, MaxSequenceLength);

        var inputIdsTensor = new DenseTensor<long>(new[] { 1, len });
        var attMaskTensor = new DenseTensor<long>(new[] { 1, len });
        var tokenTypeTensor = new DenseTensor<long>(new[] { 1, len });

        for (int i = 0; i < len; i++)
        {
            inputIdsTensor[0, i] = ids[i];
            attMaskTensor[0, i] = 1;
            tokenTypeTensor[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeTensor)
        };

        using var results = _session.Run(inputs);

        // Output: last_hidden_state shape [1, seq_len, 384] — mean pool over tokens
        var output = results.First().AsEnumerable<float>().ToArray();
        var dims = 384;
        var pooled = new float[dims];

        for (int i = 0; i < len; i++)
        {
            for (int d = 0; d < dims; d++)
                pooled[d] += output[i * dims + d];
        }

        for (int d = 0; d < dims; d++)
            pooled[d] /= len;

        Normalise(pooled);
        return pooled;
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private static void Normalise(float[] v)
    {
        float mag = 0f;
        foreach (var x in v) mag += x * x;
        mag = MathF.Sqrt(mag);
        if (mag < 1e-8f) return;
        for (int i = 0; i < v.Length; i++)
            v[i] /= mag;
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    public void Dispose() => _session.Dispose();
}
