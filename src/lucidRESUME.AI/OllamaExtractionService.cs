using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>
/// Uses a small, fast Ollama model (default: qwen3.5:0.8b) to recover structured
/// fields that the rule-based parser could not extract.
/// </summary>
public sealed class OllamaExtractionService : ILlmExtractionService
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaExtractionService> _logger;

    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public OllamaExtractionService(
        HttpClient http,
        IOptions<OllamaOptions> options,
        ILogger<OllamaExtractionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> ExtractSkillsAsync(string text, CancellationToken ct = default)
    {
        // Truncate to keep the prompt fast on the tiny model
        var input = text.Length > 3000 ? text[..3000] : text;
        var prompt = $"List all technical skills (programming languages, frameworks, tools, databases) " +
                     $"mentioned in the following resume text. " +
                     $"Reply with ONLY a comma-separated list, no explanation, no numbering.\n\n{input}";
        return await CallAsync(prompt, ct);
    }

    public async Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default)
    {
        var input = text.Length > 3000 ? text[..3000] : text;
        var prompt = $"Extract work experience from this resume. " +
                     $"For each job write one line: COMPANY | TITLE | START_YEAR - END_YEAR\n\n{input}";
        return await CallAsync(prompt, ct);
    }

    private async Task<string?> CallAsync(string prompt, CancellationToken ct)
    {
        try
        {
            // stream=true avoids qwen3.x buffering all <think> tokens before returning
            // think=false disables chain-of-thought; num_ctx=4096 keeps VRAM usage low
            var request = new { model = _options.ExtractionModel, prompt, stream = true, think = false,
                options = new { num_ctx = 4096 } };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(55)); // Allow time for first-load warm-up

            var response = await _http.PostAsJsonAsync($"{_options.BaseUrl}/api/generate", request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("LLM extraction returned {Status}", response.StatusCode);
                return null;
            }

            // Read NDJSON stream: each line is {"response":"token","done":false}
            var sb = new StringBuilder();
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line);
                if (chunk is null) continue;
                if (!string.IsNullOrEmpty(chunk.Response))
                    sb.Append(chunk.Response);
                if (chunk.Done || sb.Length > 2000) break; // cap to avoid runaway generation
            }

            _isAvailable = true;
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM extraction failed (model={Model})", _options.ExtractionModel);
            _isAvailable = false;
            return null;
        }
    }

    private record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}
