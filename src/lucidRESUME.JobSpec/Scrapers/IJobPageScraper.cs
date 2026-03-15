namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// A single layer in the layered URL scraping strategy.
/// </summary>
public interface IJobPageScraper
{
    /// <summary>
    /// Human-readable name for logging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns true if this scraper is configured and eligible to run for the given URL.
    /// </summary>
    bool CanHandle(Uri uri);

    /// <summary>
    /// Attempts to retrieve the job page content as markdown.
    /// Returns null if the scraper should be skipped or failed non-fatally.
    /// </summary>
    Task<ScrapeResult?> ScrapeAsync(Uri uri, CancellationToken ct = default);
}

/// <summary>
/// Result from a successful scrape attempt.
/// </summary>
public sealed class ScrapeResult
{
    public required string Markdown { get; init; }

    /// <summary>
    /// Structured data extracted from JSON-LD / OpenGraph, if available.
    /// </summary>
    public StructuredJobData? StructuredData { get; init; }
}

/// <summary>
/// Structured job metadata extracted from JSON-LD or OpenGraph.
/// </summary>
public sealed class StructuredJobData
{
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public bool? IsRemote { get; set; }
    public string? EmploymentType { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryCurrency { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
}
