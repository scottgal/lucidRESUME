using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace lucidRESUME.AI;

/// <summary>
/// Fully local embedding service using all-MiniLM-L6-v2 ONNX model (384 dimensions).
/// No external services required - runs on CPU via ONNX Runtime.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly string _modelPath;
    private readonly string _vocabPath;
    private readonly ConcurrentDictionary<string, float[]> _cache = new();
    private string? _failedModelSignature;
    private static readonly object LoadLock = new();
    private const int MaxCacheEntries = 500;
    private const int MaxSequenceLength = 256;

    public OnnxEmbeddingService(IOptions<EmbeddingOptions> options, ILogger<OnnxEmbeddingService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _modelPath = ResolvePath(opts.OnnxModelPath);
        _vocabPath = ResolvePath(opts.VocabPath);

        // Always defer parsing the model until the first embedding request. A partial,
        // corrupt, or in-progress download must not prevent the desktop app starting.
        // StartupHealthCheck can repair the files while lexical matching remains usable.
    }

    private bool TryEnsureLoaded()
    {
        if (_session != null && _tokenizer != null) return true;
        var signature = ModelSignature();
        if (signature == _failedModelSignature) return false;

        lock (LoadLock)
        {
            if (_session != null && _tokenizer != null) return true;
            signature = ModelSignature();
            if (signature == _failedModelSignature) return false;

            try
            {
                if (!File.Exists(_modelPath) || !File.Exists(_vocabPath))
                    throw new FileNotFoundException("The ONNX model or vocabulary is not installed.");
                LoadModel();
                _failedModelSignature = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       OnnxRuntimeException or InvalidOperationException or ArgumentException)
            {
                _session?.Dispose();
                _session = null;
                _tokenizer = null;
                _failedModelSignature = signature;
                _logger.LogWarning(ex,
                    "ONNX embeddings are unavailable; using deterministic lexical vectors until the model files change");
                return false;
            }
        }
    }

    private void LoadModel()
    {
        lock (LoadLock)
        {
            _session ??= new InferenceSession(_modelPath);
            if (_tokenizer == null)
            {
                var cleanVocab = DeduplicateVocab(_vocabPath);
                _tokenizer = BertTokenizer.Create(cleanVocab, new BertOptions { LowerCaseBeforeTokenization = true });
            }
        }
        _logger.LogInformation("ONNX embedding model loaded from {Path} (384-dim)", _modelPath);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(text, out var cached))
            return Task.FromResult(cached);

        var result = TryEnsureLoaded() ? Embed(text) : EmbedLexically(text);

        // Evict ~10% when cache full
        if (_cache.Count >= MaxCacheEntries)
        {
            var keys = _cache.Keys.Take(MaxCacheEntries / 10).ToList();
            foreach (var k in keys) _cache.TryRemove(k, out _);
        }
        _cache[text] = result;

        return Task.FromResult(result);
    }

    private string ModelSignature()
    {
        static string FileSignature(string path)
        {
            if (!File.Exists(path)) return "missing";
            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }

        return $"{FileSignature(_modelPath)}|{FileSignature(_vocabPath)}";
    }

    private static float[] EmbedLexically(string text)
    {
        const int dimensions = 384;
        var vector = new float[dimensions];
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9][a-z0-9+#.\-]*"))
        {
            var hash = Fnv1a(match.Value);
            var bucket = (int)(hash % dimensions);
            vector[bucket] += (hash & 0x80000000) == 0 ? 1f : -1f;
        }

        Normalise(vector);
        return vector;
    }

    private static uint Fnv1a(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private float[] Embed(string text)
    {
        // Tokenize with special tokens [CLS] ... [SEP]
        var ids = _tokenizer!.EncodeToIds(text, addSpecialTokens: true);
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

        using var results = _session!.Run(inputs);

        // Output: last_hidden_state shape [1, seq_len, 384] - mean pool over tokens
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

    /// <summary>
    /// Some HuggingFace vocab.txt files contain duplicate token entries (e.g. '-' at two line positions).
    /// BertTokenizer.Create uses a Dictionary which throws on duplicate keys.
    /// This writes a deduplicated copy to a temp file, keeping only the first occurrence of each token.
    /// </summary>
    private static string DeduplicateVocab(string vocabPath)
    {
        var lines = File.ReadAllLines(vocabPath);
        var seen = new HashSet<string>();
        bool hasDuplicates = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!seen.Add(lines[i]))
            {
                hasDuplicates = true;
                break;
            }
        }

        if (!hasDuplicates) return vocabPath;

        // Write deduplicated version — replace duplicate lines with [unused{line}] placeholder
        // to preserve line numbering (token IDs = line numbers in BERT vocab)
        seen.Clear();
        var cleanLines = new string[lines.Length];
        int unusedIdx = 9000; // high unused range
        for (int i = 0; i < lines.Length; i++)
        {
            if (seen.Add(lines[i]))
                cleanLines[i] = lines[i];
            else
                cleanLines[i] = $"[unused_dedup_{unusedIdx++}]";
        }

        var cleanPath = Path.Combine(Path.GetDirectoryName(vocabPath)!, "vocab_clean.txt");
        File.WriteAllLines(cleanPath, cleanLines);
        return cleanPath;
    }

    private static string ResolvePath(string path) =>
        AppDataPaths.Resolve(path);

    public void Dispose() => _session?.Dispose();
}
