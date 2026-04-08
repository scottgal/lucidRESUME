using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class AnthropicExtractionService : ILlmExtractionService
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicExtractionService> _logger;

    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public AnthropicExtractionService(HttpClient http, IOptions<AnthropicOptions> options,
        ILogger<AnthropicExtractionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
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
        var prompt = "Extract ONLY the work experience entries from this resume text. " +
                     "Each job held should be ONE line: COMPANY_NAME | JOB_TITLE | START_DATE - END_DATE. " +
                     "Do NOT include bullet points, achievements, or education. " +
                     "If company unknown write 'Unknown'. Reply with ONLY the formatted lines.\n\n" + input;
        return CallAsync(prompt, ct);
    }

    public async Task<string?> ExtractNameAsync(string headerText, CancellationToken ct = default)
    {
        var input = headerText.Length > 500 ? headerText[..500] : headerText;
        var prompt = "What is the person's full name in this resume header? Reply with ONLY the name, nothing else. " +
                     "If you cannot determine the name, reply with just \"UNKNOWN\".\n\n" + input;
        var result = await CallAsync(prompt, ct);
        if (string.IsNullOrWhiteSpace(result) || result.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            return null;
        return result.Trim('"', '\'', '.', ' ', '\n', '\r');
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

            var response = await _http.PostAsJsonAsync("v1/messages", request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: cts.Token);
            _isAvailable = true;
            return result?.Content?.FirstOrDefault()?.Text?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Anthropic extraction failed");
            _isAvailable = false;
            return null;
        }
    }

    private record AnthropicResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);
    private record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
