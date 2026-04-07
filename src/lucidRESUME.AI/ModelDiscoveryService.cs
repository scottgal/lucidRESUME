using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>
/// Discovers available models from Ollama, Anthropic, and OpenAI.
/// </summary>
public sealed class ModelDiscoveryService
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _ollamaOpts;
    private readonly AnthropicOptions _anthropicOpts;
    private readonly OpenAiOptions _openAiOpts;
    private readonly ILogger<ModelDiscoveryService> _logger;

    // Approximate cost per 1M input tokens (USD) - for display only
    private static readonly Dictionary<string, string> KnownCosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic
        ["claude-opus-4-6"] = "$15/1M in, $75/1M out",
        ["claude-sonnet-4-6"] = "$3/1M in, $15/1M out",
        ["claude-haiku-4-5-20251001"] = "$0.80/1M in, $4/1M out",
        ["claude-sonnet-4-5-20250929"] = "$3/1M in, $15/1M out",
        ["claude-opus-4-1-20250805"] = "$15/1M in, $75/1M out",
        ["claude-3-haiku-20240307"] = "$0.25/1M in, $1.25/1M out",
        // OpenAI
        ["gpt-4o"] = "$2.50/1M in, $10/1M out",
        ["gpt-4o-mini"] = "$0.15/1M in, $0.60/1M out",
        ["gpt-4-turbo"] = "$10/1M in, $30/1M out",
        ["gpt-3.5-turbo"] = "$0.50/1M in, $1.50/1M out",
        ["o1"] = "$15/1M in, $60/1M out",
        ["o1-mini"] = "$1.10/1M in, $4.40/1M out",
        ["o3-mini"] = "$1.10/1M in, $4.40/1M out",
    };

    public ModelDiscoveryService(
        HttpClient http,
        IOptions<OllamaOptions> ollamaOpts,
        IOptions<AnthropicOptions> anthropicOpts,
        IOptions<OpenAiOptions> openAiOpts,
        ILogger<ModelDiscoveryService> logger)
    {
        _http = http;
        _ollamaOpts = ollamaOpts.Value;
        _anthropicOpts = anthropicOpts.Value;
        _openAiOpts = openAiOpts.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListOllamaModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<OllamaTagsResponse>(
                $"{_ollamaOpts.BaseUrl}/api/tags", ct);
            return resp?.Models?
                .Select(m => new ModelInfo(m.Name, m.Name, "ollama", "Free (local)"))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list Ollama models");
            return [];
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAnthropicModelsAsync(CancellationToken ct = default)
    {
        if (!_anthropicOpts.IsConfigured) return [];
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
            req.Headers.Add("x-api-key", _anthropicOpts.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<AnthropicModelsResponse>(cancellationToken: ct);
            return result?.Data?
                .Select(m => new ModelInfo(
                    m.Id, m.Id, "anthropic",
                    KnownCosts.GetValueOrDefault(m.Id, "See pricing page")))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list Anthropic models");
            return [];
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListOpenAiModelsAsync(CancellationToken ct = default)
    {
        if (!_openAiOpts.IsConfigured) return [];
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_openAiOpts.BaseUrl.TrimEnd('/')}/models");
            req.Headers.Add("Authorization", $"Bearer {_openAiOpts.ApiKey}");

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<OpenAiModelsResponse>(cancellationToken: ct);
            return result?.Data?
                .Where(m => m.Id.StartsWith("gpt") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3"))
                .OrderBy(m => m.Id)
                .Select(m => new ModelInfo(
                    m.Id, m.Id, "openai",
                    KnownCosts.GetValueOrDefault(m.Id, "See pricing page")))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list OpenAI models");
            return [];
        }
    }

    // Response DTOs
    private record OllamaTagsResponse(
        [property: JsonPropertyName("models")] List<OllamaModel>? Models);
    private record OllamaModel(
        [property: JsonPropertyName("name")] string Name);
    private record AnthropicModelsResponse(
        [property: JsonPropertyName("data")] List<AnthropicModel>? Data);
    private record AnthropicModel(
        [property: JsonPropertyName("id")] string Id);
    private record OpenAiModelsResponse(
        [property: JsonPropertyName("data")] List<OpenAiModel>? Data);
    private record OpenAiModel(
        [property: JsonPropertyName("id")] string Id);
}