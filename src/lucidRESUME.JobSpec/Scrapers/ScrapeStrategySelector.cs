using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Selects the ordered list of scrapers to try for a given URL.
/// Applies domain-level overrides before falling back to the full layer stack.
/// </summary>
public sealed class ScrapeStrategySelector
{
    // Domains that should skip straight to Layer 5 (manual paste).
    private static readonly HashSet<string> ManualOnlyDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "linkedin.com",
        "glassdoor.com"
    };

    private readonly CloudflareMarkdownHeaderScraper _layer1;
    private readonly HttpMarkdownScraper _layer2;
    private readonly CloudflareBrScraper _layer3;
    private readonly PlaywrightScraper _layer4;
    private readonly CloudflareBrOptions _cfBrOptions;
    private readonly ILogger<ScrapeStrategySelector> _logger;

    public ScrapeStrategySelector(
        CloudflareMarkdownHeaderScraper layer1,
        HttpMarkdownScraper layer2,
        CloudflareBrScraper layer3,
        PlaywrightScraper layer4,
        IOptions<CloudflareBrOptions> cfBrOptions,
        ILogger<ScrapeStrategySelector> logger)
    {
        _layer1 = layer1;
        _layer2 = layer2;
        _layer3 = layer3;
        _layer4 = layer4;
        _cfBrOptions = cfBrOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns an ordered sequence of scrapers to attempt for the given URL,
    /// or an empty sequence if the domain requires manual paste (Layer 5).
    /// </summary>
    public IReadOnlyList<IJobPageScraper> SelectScrapers(Uri uri)
    {
        var host = uri.Host.TrimStart('w', '.');

        foreach (var domain in ManualOnlyDomains)
        {
            if (host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Domain {Host} matched manual-only list — skipping to Layer 5 (manual paste).", uri.Host);
                return [];
            }
        }

        // Build the ordered stack based on configuration.
        var scrapers = new List<IJobPageScraper> { _layer1, _layer2 };

        if (_cfBrOptions.IsConfigured)
        {
            // Layer 3 configured — prefer it over Playwright
            scrapers.Add(_layer3);
        }
        else
        {
            // Layer 4 — local Playwright, heavy but available
            scrapers.Add(_layer4);
        }

        return scrapers;
    }
}
