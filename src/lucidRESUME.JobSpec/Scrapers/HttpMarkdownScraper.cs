using Microsoft.Extensions.Logging;
using ReverseMarkdown;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Layer 2: Plain HTTP GET, convert HTML → markdown via ReverseMarkdown,
/// and extract JSON-LD / OpenGraph structured data from the same response.
/// </summary>
public sealed class HttpMarkdownScraper : IJobPageScraper
{
    private readonly HttpClient _http;
    private readonly StructuredDataExtractor _structuredDataExtractor;
    private readonly ILogger<HttpMarkdownScraper> _logger;

    private static readonly Converter _mdConverter = new(new Config
    {
        UnknownTags = Config.UnknownTagsOption.PassThrough,
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    public HttpMarkdownScraper(
        HttpClient http,
        StructuredDataExtractor structuredDataExtractor,
        ILogger<HttpMarkdownScraper> logger)
    {
        _http = http;
        _structuredDataExtractor = structuredDataExtractor;
        _logger = logger;
    }

    public string Name => "HTTP-Markdown";

    public bool CanHandle(Uri uri) => true; // fallback for any URL

    public async Task<ScrapeResult?> ScrapeAsync(Uri uri, CancellationToken ct = default)
    {
        string html;
        try
        {
            html = await _http.GetStringAsync(uri, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Scraper}] HTTP request failed for {Url}", Name, uri);
            return null;
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            _logger.LogDebug("[{Scraper}] Empty response for {Url}", Name, uri);
            return null;
        }

        // Convert HTML → markdown
        string markdown;
        try
        {
            markdown = _mdConverter.Convert(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Scraper}] ReverseMarkdown conversion failed for {Url}", Name, uri);
            return null;
        }

        if (markdown.Length <= 200)
        {
            _logger.LogDebug("[{Scraper}] Markdown too short ({Len} chars) for {Url}", Name, markdown.Length, uri);
            return null;
        }

        // Extract structured data from the same HTML
        StructuredJobData? structuredData = null;
        try
        {
            structuredData = await _structuredDataExtractor.ExtractAsync(html, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Scraper}] Structured data extraction failed (non-fatal) for {Url}", Name, uri);
        }

        _logger.LogInformation("[{Scraper}] Success — {Len} chars markdown for {Url}", Name, markdown.Length, uri);
        return new ScrapeResult { Markdown = markdown, StructuredData = structuredData };
    }
}
