using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Layer 3: Cloudflare Browser Rendering API.
/// Only active when CloudflareBrOptions.AccountId and ApiToken are configured.
/// POST https://api.cloudflare.com/client/v4/accounts/{AccountId}/browser-rendering/markdown
/// </summary>
public sealed class CloudflareBrScraper : IJobPageScraper
{
    private readonly HttpClient _http;
    private readonly CloudflareBrOptions _options;
    private readonly ILogger<CloudflareBrScraper> _logger;

    public CloudflareBrScraper(
        HttpClient http,
        IOptions<CloudflareBrOptions> options,
        ILogger<CloudflareBrScraper> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "CF-Browser-Rendering";

    public bool CanHandle(Uri uri) => _options.IsConfigured;

    public async Task<ScrapeResult?> ScrapeAsync(Uri uri, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogDebug("[{Scraper}] Not configured — skipping.", Name);
            return null;
        }

        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/browser-rendering/markdown";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiToken}");

        var body = new { url = uri.AbsoluteUri, gotoOptions = new { waitUntil = _options.WaitUntil } };
        request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Scraper}] API call failed for {Url}", Name, uri);
            return null;
        }

        CfBrResponse? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<CfBrResponse>(cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[{Scraper}] Failed to deserialise response for {Url}", Name, uri);
            return null;
        }

        if (result is not { Success: true } || string.IsNullOrWhiteSpace(result.Result))
        {
            _logger.LogDebug("[{Scraper}] API returned success=false or empty result for {Url}", Name, uri);
            return null;
        }

        var markdown = result.Result;
        if (markdown.Length <= 200)
        {
            _logger.LogDebug("[{Scraper}] Markdown too short ({Len} chars) for {Url}", Name, markdown.Length, uri);
            return null;
        }

        _logger.LogInformation("[{Scraper}] Success — {Len} chars for {Url}", Name, markdown.Length, uri);
        return new ScrapeResult { Markdown = markdown };
    }

    private sealed class CfBrResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }
}
