using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Layer 1: Send Accept: text/markdown header.
/// Cloudflare "Markdown for Agents" (Feb 2026) converts HTML→markdown at the CDN edge
/// for Cloudflare-hosted sites — no key, no cost, just a header.
/// </summary>
public sealed class CloudflareMarkdownHeaderScraper : IJobPageScraper
{
    private readonly HttpClient _http;
    private readonly ILogger<CloudflareMarkdownHeaderScraper> _logger;

    public CloudflareMarkdownHeaderScraper(HttpClient http, ILogger<CloudflareMarkdownHeaderScraper> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string Name => "CF-Markdown-Header";

    public bool CanHandle(Uri uri) => true; // always try first

    public async Task<ScrapeResult?> ScrapeAsync(Uri uri, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "text/markdown, text/html;q=0.9, */*;q=0.5");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Scraper}] HTTP request failed for {Url}", Name, uri);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Log Cloudflare markdown token count if present
        if (response.Headers.TryGetValues("x-markdown-tokens", out var tokenValues))
            _logger.LogDebug("[{Scraper}] x-markdown-tokens: {Tokens}", Name, string.Join(",", tokenValues));

        if (!contentType.Contains("text/markdown", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[{Scraper}] Response Content-Type was '{CT}', not text/markdown — layer skipped.", Name, contentType);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length <= 200)
        {
            _logger.LogDebug("[{Scraper}] Markdown body too short ({Len} chars) — layer skipped.", Name, body.Length);
            return null;
        }

        _logger.LogInformation("[{Scraper}] Success — received {Len} chars of markdown for {Url}", Name, body.Length, uri);
        return new ScrapeResult { Markdown = body };
    }
}
