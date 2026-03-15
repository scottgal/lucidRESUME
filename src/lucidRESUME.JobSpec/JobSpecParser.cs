using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobSpec.Scrapers;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSpec;

public sealed class JobSpecParser : IJobSpecParser
{
    private readonly ILogger<JobSpecParser> _logger;
    private readonly ScrapeStrategySelector _strategySelector;

    public JobSpecParser(ILogger<JobSpecParser> logger, ScrapeStrategySelector strategySelector)
    {
        ArgumentNullException.ThrowIfNull(strategySelector);
        _logger = logger;
        _strategySelector = strategySelector;
    }

    /// <summary>Constructor for text-only use (no URL scraping). Tests use this.</summary>
    internal JobSpecParser(ILogger<JobSpecParser> logger)
    {
        _logger = logger;
        _strategySelector = null!;
    }

    public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default)
    {
        var job = JobDescription.Create(text, new JobSource { Type = JobSourceType.PastedText });
        ExtractFields(job, text);
        return Task.FromResult(job);
    }

    public async Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));

        var scrapers = _strategySelector.SelectScrapers(uri);

        // Empty scraper list = manual-only domain (Layer 5)
        if (scrapers.Count == 0)
        {
            _logger.LogWarning("URL {Url} requires manual paste (login wall / bot detection).", url);
            var manualJob = JobDescription.Create("", new JobSource { Type = JobSourceType.Url, Url = url });
            manualJob.MarkNeedsManualInput();
            return manualJob;
        }

        ScrapeResult? result = null;
        foreach (var scraper in scrapers)
        {
            if (!scraper.CanHandle(uri))
            {
                _logger.LogDebug("Scraper {Scraper} reports CanHandle=false for {Url} — skipping.", scraper.Name, url);
                continue;
            }

            _logger.LogDebug("Trying scraper {Scraper} for {Url}...", scraper.Name, url);
            try
            {
                result = await scraper.ScrapeAsync(uri, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scraper {Scraper} threw an unhandled exception for {Url}.", scraper.Name, url);
            }

            if (result is not null)
            {
                _logger.LogInformation("Scraper {Scraper} succeeded for {Url}.", scraper.Name, url);
                break;
            }
        }

        // Layer 5 fallback — all scrapers failed
        if (result is null)
        {
            _logger.LogWarning("All scrapers failed for {Url} — returning NeedsManualInput.", url);
            var fallback = JobDescription.Create("", new JobSource { Type = JobSourceType.Url, Url = url });
            fallback.MarkNeedsManualInput();
            return fallback;
        }

        var job = JobDescription.Create(result.Markdown, new JobSource { Type = JobSourceType.Url, Url = url });
        ExtractFields(job, result.Markdown);
        ApplyStructuredData(job, result.StructuredData);
        return job;
    }

    // -------------------------------------------------------------------------
    // Structured data overlay — enriches what regex couldn't find
    // -------------------------------------------------------------------------

    private static void ApplyStructuredData(JobDescription job, StructuredJobData? data)
    {
        if (data is null) return;

        if (string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(data.Title))
            job.Title = data.Title;

        if (string.IsNullOrWhiteSpace(job.Company) && !string.IsNullOrWhiteSpace(data.Company))
            job.Company = data.Company;

        if (string.IsNullOrWhiteSpace(job.Location) && !string.IsNullOrWhiteSpace(data.Location))
            job.Location = data.Location;

        if (job.IsRemote is null && data.IsRemote.HasValue)
            job.IsRemote = data.IsRemote;

        if (job.Salary is null && data.SalaryMin.HasValue)
            job.Salary = new SalaryRange(data.SalaryMin.Value, data.SalaryMax ?? data.SalaryMin.Value);

        // OG fallbacks for title only
        if (string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(data.OgTitle))
            job.Title = data.OgTitle;
    }

    // -------------------------------------------------------------------------
    // Field extraction from plain text / markdown
    // -------------------------------------------------------------------------

    private static void ExtractFields(JobDescription job, string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Title + Company from first meaningful line: "Title at Company" or "Title - Company"
        var header = lines.FirstOrDefault(l => l.Length > 5);
        if (header != null)
        {
            var atSplit = header.Split(" at ", 2, StringSplitOptions.TrimEntries);
            var dashSplit = header.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (atSplit.Length == 2) { job.Title = atSplit[0]; job.Company = atSplit[1]; }
            else if (dashSplit.Length == 2) { job.Title = dashSplit[0]; job.Company = dashSplit[1]; }
        }

        // Remote detection
        var lower = text.ToLowerInvariant();
        job.IsRemote = lower.Contains("fully remote") || lower.Contains("remote: yes") || lower.Contains("100% remote");
        job.IsHybrid = lower.Contains("hybrid");

        // Skills extraction
        job.RequiredSkills = ExtractSkillsList(text, @"required|skills");
        job.PreferredSkills = ExtractSkillsList(text, @"preferred|nice to have|desirable");

        // Salary via regex: £60,000 - £80,000 or $60,000 - $80,000
        var salaryMatch = Regex.Match(text, @"[£$€](\d[\d,]+)\s*[-–]\s*[£$€]?(\d[\d,]+)", RegexOptions.IgnoreCase);
        if (salaryMatch.Success)
        {
            var min = decimal.Parse(salaryMatch.Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
            var max = decimal.Parse(salaryMatch.Groups[2].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
            job.Salary = new SalaryRange(min, max);
        }
    }

    private static List<string> ExtractSkillsList(string text, string sectionPattern)
    {
        var skills = new List<string>();
        var sectionRegex = new Regex($@"(?:{sectionPattern})[:\s]+([^\n]+)", RegexOptions.IgnoreCase);
        foreach (Match m in sectionRegex.Matches(text))
        {
            var items = m.Groups[1].Value.Split([',', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries);
            skills.AddRange(items.Select(s => s.Trim()).Where(s => s.Length > 1));
        }
        return skills;
    }
}
