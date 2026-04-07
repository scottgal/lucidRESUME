using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobSpec.Extraction;
using lucidRESUME.JobSpec.Scrapers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.JobSpec;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobSpec(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // --- Cloudflare Browser Rendering options ---
        if (configuration is not null)
            services.Configure<CloudflareBrOptions>(
                configuration.GetSection(CloudflareBrOptions.SectionName));
        else
            services.Configure<CloudflareBrOptions>(_ => { }); // bind empty defaults

        // --- Shared HttpClient for simple scrapers ---
        // Named client with a browser-like User-Agent so plain HTTP fetches succeed
        services.AddHttpClient("JobSpecScraper", c =>
        {
            c.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; lucidRESUME/1.0; +https://github.com/scottgal/lucidRESUME)");
            c.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        // --- Scraper registrations ---

        // Layer 1: CF Markdown Header
        services.AddTransient<CloudflareMarkdownHeaderScraper>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("JobSpecScraper");
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CloudflareMarkdownHeaderScraper>>();
            return new CloudflareMarkdownHeaderScraper(http, logger);
        });

        // Layer 2: HTTP + ReverseMarkdown + JSON-LD
        services.AddTransient<StructuredDataExtractor>();
        services.AddTransient<HttpMarkdownScraper>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("JobSpecScraper");
            var extractor = sp.GetRequiredService<StructuredDataExtractor>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpMarkdownScraper>>();
            return new HttpMarkdownScraper(http, extractor, logger);
        });

        // Layer 3: CF Browser Rendering API
        services.AddTransient<CloudflareBrScraper>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("JobSpecScraper");
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudflareBrOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CloudflareBrScraper>>();
            return new CloudflareBrScraper(http, opts, logger);
        });

        // Layer 4: Local Playwright (singleton - shares browser instance)
        services.AddSingleton<PlaywrightScraper>();

        // Strategy selector
        services.AddTransient<ScrapeStrategySelector>();

        // --- Fusion options ---
        if (configuration is not null)
            services.Configure<FusionOptions>(configuration.GetSection("JdFusion"));

        // --- Main parser ---
        services.AddTransient<IJobSpecParser, JobSpecParser>();

        return services;
    }
}