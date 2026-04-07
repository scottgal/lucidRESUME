using System.Net.Http.Json;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>
/// Calls Ollama's /api/embeddings endpoint to generate dense vectors for text.
/// Uses nomic-embed-text by default (768 dimensions, runs on CPU fine).
/// Includes a simple in-process LRU cache to avoid re-embedding the same strings.
/// </summary>
public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingService> _logger;

    // Simple concurrent LRU cache - keyed by (model, text)
    private readonly Dictionary<(string model, string text), float[]> _cache = new();
    private const int MaxCacheEntries = 500;

    public OllamaEmbeddingService(HttpClient http, IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingService> logger)
    {
        _http    = http;
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var key = (_options.EmbeddingModel, text);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        _logger.LogDebug("Embedding text ({Length} chars) with {Model}", text.Length, _options.EmbeddingModel);

        var request  = new { model = _options.EmbeddingModel, prompt = text };
        var response = await _http.PostAsJsonAsync($"{_options.BaseUrl}/api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Ollama returned null embedding response");

        var vector = result.Embedding;
        Normalise(vector);

        // Evict oldest entry if cache full
        if (_cache.Count >= MaxCacheEntries)
        {
            var first = _cache.Keys.First();
            _cache.Remove(first);
        }
        _cache[key] = vector;
        return vector;
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        // Both vectors are pre-normalised → cosine similarity = dot product
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

    private record OllamaEmbeddingResponse(float[] Embedding);
}