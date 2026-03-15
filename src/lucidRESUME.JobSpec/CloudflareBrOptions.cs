namespace lucidRESUME.JobSpec;

/// <summary>
/// Configuration for the Cloudflare Browser Rendering API (Layer 3 scraper).
/// Set AccountId and ApiToken in appsettings.json under "CloudflareBrowserRendering".
/// </summary>
public sealed class CloudflareBrOptions
{
    public const string SectionName = "CloudflareBrowserRendering";

    public string AccountId { get; set; } = "";
    public string ApiToken { get; set; } = "";
    /// <summary>
    /// Puppeteer waitUntil value — default is "networkidle0".
    /// </summary>
    public string WaitUntil { get; set; } = "networkidle0";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(ApiToken);
}
