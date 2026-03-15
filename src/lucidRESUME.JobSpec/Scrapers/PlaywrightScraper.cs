using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ReverseMarkdown;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Layer 4: Local Playwright headless browser.
/// Used when CF Browser Rendering API (Layer 3) is not configured.
/// Heavy — lazy-initialises Playwright on first use.
///
/// SETUP: Run "playwright install chromium" after package restore.
/// The easiest place is in a post-publish script or user-facing setup wizard.
/// </summary>
public sealed class PlaywrightScraper : IJobPageScraper, IAsyncDisposable
{
    private readonly StructuredDataExtractor _structuredDataExtractor;
    private readonly ILogger<PlaywrightScraper> _logger;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly Converter _mdConverter = new(new Config
    {
        UnknownTags = Config.UnknownTagsOption.PassThrough,
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    public PlaywrightScraper(
        StructuredDataExtractor structuredDataExtractor,
        ILogger<PlaywrightScraper> logger)
    {
        _structuredDataExtractor = structuredDataExtractor;
        _logger = logger;
    }

    public string Name => "Playwright";

    // Only used when CF BR is not configured — callers should check CloudflareBrScraper.CanHandle first.
    public bool CanHandle(Uri uri) => true;

    public async Task<ScrapeResult?> ScrapeAsync(Uri uri, CancellationToken ct = default)
    {
        await EnsureInitialisedAsync(ct);

        if (_browser is null)
        {
            _logger.LogWarning("[{Scraper}] Browser not available — skipping.", Name);
            return null;
        }

        IPage page;
        try
        {
            page = await _browser.NewPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Scraper}] Failed to open new page for {Url}", Name, uri);
            return null;
        }

        try
        {
            await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });

            var html = await page.ContentAsync();

            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogDebug("[{Scraper}] Empty page content for {Url}", Name, uri);
                return null;
            }

            var markdown = _mdConverter.Convert(html);
            if (markdown.Length <= 200)
            {
                _logger.LogDebug("[{Scraper}] Markdown too short ({Len} chars) for {Url}", Name, markdown.Length, uri);
                return null;
            }

            StructuredJobData? structuredData = null;
            try
            {
                structuredData = await _structuredDataExtractor.ExtractAsync(html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[{Scraper}] Structured data extraction failed (non-fatal) for {Url}", Name, uri);
            }

            _logger.LogInformation("[{Scraper}] Success — {Len} chars for {Url}", Name, markdown.Length, uri);
            return new ScrapeResult { Markdown = markdown, StructuredData = structuredData };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Scraper}] Page navigation/content failed for {Url}", Name, uri);
            return null;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task EnsureInitialisedAsync(CancellationToken ct)
    {
        if (_browser is not null) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser is not null) return;

            _logger.LogInformation("[{Scraper}] Lazy-initialising Playwright (chromium)...", Name);
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
            _logger.LogInformation("[{Scraper}] Playwright browser ready.", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Scraper}] Failed to initialise Playwright. Ensure 'playwright install chromium' has been run.", Name);
            // leave _browser null so CanHandle gracefully returns null
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
