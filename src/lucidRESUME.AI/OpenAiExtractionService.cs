using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class OpenAiExtractionService : ILlmExtractionService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiExtractionService> _logger;

    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public OpenAiExtractionService(HttpClient http, IOptions<OpenAiOptions> options,
        ILogger<OpenAiExtractionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
    }

    public Task<string?> ExtractSkillsAsync(string text, CancellationToken ct = default)
    {
        var input = text.Length > 6000 ? text[..6000] : text;
        var prompt = "List all technical skills (programming languages, frameworks, tools, databases) " +
                     "mentioned in the following resume text. " +
                     "Reply with ONLY a comma-separated list, no explanation, no numbering.\n\n" + input;
        return CallAsync(prompt, ct);
    }

    public Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default)
    {
        var input = text.Length > 6000 ? text[..6000] : text;
        var prompt = "Extract work experience from this resume. " +
                     "For each job write one line: COMPANY | TITLE | START_YEAR - END_YEAR\n\n" + input;
        return CallAsync(prompt, ct);
    }

    private async Task<string?> CallAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                model = _options.ExtractionModel,
                max_tokens = 2000,
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(55));

            var response = await _http.PostAsJsonAsync("chat/completions", request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cts.Token);
            _isAvailable = true;
            return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenAI extraction failed");
            _isAvailable = false;
            return null;
        }
    }

    private record OpenAiResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);
    private record Choice(
        [property: JsonPropertyName("message")] Msg? Message);
    private record Msg(
        [property: JsonPropertyName("content")] string? Content);
}
