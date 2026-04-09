using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobSpec.Extraction;
using lucidRESUME.JobSpec.Scrapers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSpec;

public sealed class JobSpecParser : IJobSpecParser
{
    private readonly ILogger<JobSpecParser> _logger;
    private readonly ScrapeStrategySelector _strategySelector;
    private readonly IEnumerable<IEntityDetector>? _detectors;
    private readonly ILlmExtractionService? _llm;
    private readonly ISkillTaxonomy? _taxonomy;
    private readonly IEntityLookup? _entityLookup;
    private readonly FusionOptions _fusionOpts;

    public JobSpecParser(ILogger<JobSpecParser> logger, ScrapeStrategySelector strategySelector,
        IEnumerable<IEntityDetector> detectors, IOptions<FusionOptions>? fusionOpts = null,
        ILlmExtractionService? llm = null, ISkillTaxonomy? taxonomy = null,
        IEntityLookup? entityLookup = null)
    {
        ArgumentNullException.ThrowIfNull(strategySelector);
        _logger = logger;
        _strategySelector = strategySelector;
        _detectors = detectors;
        _llm = llm;
        _taxonomy = taxonomy;
        _entityLookup = entityLookup;
        _fusionOpts = fusionOpts?.Value ?? new FusionOptions();
    }

    /// <summary>Constructor for text-only use (no URL scraping). Tests use this.</summary>
    internal JobSpecParser(ILogger<JobSpecParser> logger)
    {
        _logger = logger;
        _strategySelector = null!;
    }

    public async Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default)
    {
        var job = JobDescription.Create(text, new JobSource { Type = JobSourceType.PastedText });

        // Run all extractors in parallel — each produces candidates with confidence
        var allCandidates = new List<JdFieldCandidate>();

        // Layer 1: Structural (fast, high confidence for obvious patterns)
        allCandidates.AddRange(StructuralExtractor.Extract(text));

        // Layer 2+3+4: NER + LLM + Taxonomy centroids — ALL run in parallel
        // LLM always runs if available (don't gate on IsAvailable — let it try and fail gracefully)
        var tasks = new List<Task<List<JdFieldCandidate>>>();
        if (_detectors is not null)
            tasks.Add(NerExtractor.ExtractAsync(text, _detectors, ct));
        if (_llm is not null)
            tasks.Add(LlmExtractor.ExtractAsync(text, _llm, ct));
        if (_taxonomy is not null)
            tasks.Add(TaxonomyCentroidExtractAsync(text, ct));

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
            allCandidates.AddRange(r);

        // Layer 5: Entity lookup — find known companies/locations in text
        if (_entityLookup is not null)
        {
            foreach (var company in _entityLookup.FindCompaniesInText(text))
                allCandidates.Add(new("company", company, 0.80, "entity-lookup"));
            foreach (var location in _entityLookup.FindLocationsInText(text))
                allCandidates.Add(new("location", location, 0.70, "entity-lookup"));
        }

        // Fuse all signals via RRF
        var fused = JdFieldFuser.Fuse(allCandidates, _fusionOpts);
        ApplyFusedFields(job, fused);

        _logger.LogInformation(
            "JD parsed: {Title} at {Company}, {SkillCount} skills ({PreferredCount} preferred), {Total} candidates from {Layers} layers",
            job.Title, job.Company, job.RequiredSkills.Count, job.PreferredSkills.Count,
            allCandidates.Count, allCandidates.Select(c => c.Source.Split(':')[0]).Distinct().Count());

        return job;
    }

    /// <summary>Layer 4: Taxonomy centroid matching — finds known skills in text.</summary>
    private async Task<List<JdFieldCandidate>> TaxonomyCentroidExtractAsync(string text, CancellationToken ct)
    {
        var candidates = new List<JdFieldCandidate>();
        if (_taxonomy is null) return candidates;

        try
        {
            // Fast exact match against 5,630 known skills
            var exactMatches = _taxonomy.FindSkillsExact(text);
            foreach (var skill in exactMatches)
                candidates.Add(new("skill", skill, 0.75, "taxonomy"));
        }
        catch { /* taxonomy failure is non-fatal */ }

        return candidates;
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
                _logger.LogDebug("Scraper {Scraper} reports CanHandle=false for {Url} - skipping.", scraper.Name, url);
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

        // Layer 5 fallback - all scrapers failed
        if (result is null)
        {
            _logger.LogWarning("All scrapers failed for {Url} - returning NeedsManualInput.", url);
            var fallback = JobDescription.Create("", new JobSource { Type = JobSourceType.Url, Url = url });
            fallback.MarkNeedsManualInput();
            return fallback;
        }

        var job = JobDescription.Create(result.Markdown, new JobSource { Type = JobSourceType.Url, Url = url });
        ExtractFields(job, result.Markdown);
        ApplyStructuredData(job, result.StructuredData);
        return job;
    }

    private void ApplyFusedFields(JobDescription job, FusedJdFields fused)
    {
        if (fused.Title is not null) job.Title = fused.Title.Value;
        // Accept company if: high confidence, OR if entity lookup confirms it's a real company
        if (fused.Company is not null)
        {
            var isKnown = _entityLookup?.IsKnownCompany(fused.Company.Value) ?? false;
            if (fused.Company.Confidence >= 0.70 || isKnown)
                job.Company = fused.Company.Value;
        }
        if (fused.Location is not null) job.Location = fused.Location.Value;
        if (fused.IsRemote) job.IsRemote = true;
        if (fused.IsHybrid) job.IsHybrid = true;
        if (fused.YearsExperience.HasValue) job.RequiredYearsExperience = fused.YearsExperience;
        if (fused.SalaryMin.HasValue)
        {
            var currency = fused.SalaryCurrency ?? "GBP";
            var period = fused.SalaryPeriod ?? "annual";
            job.Salary = new SalaryRange(fused.SalaryMin.Value, fused.SalaryMax ?? fused.SalaryMin.Value, currency, period);
        }
        if (fused.Skills.Count > 0)
            job.RequiredSkills = fused.Skills.Select(s => s.Value).ToList();
        if (fused.PreferredSkills.Count > 0)
            job.PreferredSkills = fused.PreferredSkills.Select(s => s.Value).ToList();
        if (fused.Responsibilities.Count > 0)
            job.Responsibilities = fused.Responsibilities.Select(s => s.Value).ToList();
        if (fused.Benefits.Count > 0)
            job.Benefits = fused.Benefits.Select(s => s.Value).ToList();
        if (fused.Education is not null)
            job.RequiredEducation = fused.Education.Value;
    }

    // -------------------------------------------------------------------------
    // Structured data overlay - enriches what regex couldn't find
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

    /// <summary>Legacy regex extraction - used by URL path and as supplement.</summary>
    private static void ExtractFields(JobDescription job, string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Title + Company - only if LLM didn't already extract them
        if (string.IsNullOrWhiteSpace(job.Title))
        {
            var header = lines.FirstOrDefault(l => l.Length > 5 && !l.StartsWith('#'));
            if (header != null)
            {
                string[]? titleCompany = null;
                foreach (var sep in new[] { " at ", " - ", " – ", " - ", " | " })
                {
                    var parts = header.Split(sep, 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && parts[0].Length > 2 && parts[1].Length > 2)
                    { titleCompany = parts; break; }
                }
                if (titleCompany != null)
                { job.Title = titleCompany[0].TrimStart('#').Trim(); job.Company ??= titleCompany[1]; }
                else if (header.Length < 80)
                    job.Title = header.TrimStart('#').Trim();
            }
        }

        // Remote detection - only if not already set
        var lower = text.ToLowerInvariant();
        job.IsRemote ??= lower.Contains("remote") && (
            lower.Contains("fully remote") || lower.Contains("remote:") ||
            lower.Contains("100% remote") || lower.Contains("location: remote") ||
            Regex.IsMatch(lower, @"remote\s*\("));
        job.IsHybrid ??= lower.Contains("hybrid");

        // Skills - only if LLM didn't extract enough
        if (job.RequiredSkills.Count < 3)
            job.RequiredSkills = ExtractSkillsList(text, @"required|skills");
        if (job.PreferredSkills.Count < 3)
            job.PreferredSkills = ExtractSkillsList(text, @"preferred|nice to have|desirable");

        // Salary - only if not already extracted
        if (job.Salary is null)
        {
            var salaryMatch = Regex.Match(text, @"[£$€](\d[\d,]+)\s*[-–]\s*[£$€]?(\d[\d,]+)", RegexOptions.IgnoreCase);
            if (salaryMatch.Success)
            {
                var min = decimal.Parse(salaryMatch.Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
                var max = decimal.Parse(salaryMatch.Groups[2].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
                job.Salary = new SalaryRange(min, max);
            }
        }

        // Years - simple fallback only
        if (job.RequiredYearsExperience is null)
        {
            var yearsMatch = Regex.Match(text,
                @"(\d+)\+?\s*years?\s*(?:of\s*)?experience",
                RegexOptions.IgnoreCase);
            if (yearsMatch.Success && int.TryParse(yearsMatch.Groups[1].Value, out int years))
                job.RequiredYearsExperience = years;
        }

        // Responsibilities: lines under "Responsibilities:", "You will:", "What you'll do:"
        job.Responsibilities = ExtractBulletSection(text,
            @"responsibilities|you will|what you.{0,10}do|key duties|your role");
    }

    private static List<string> ExtractSkillsList(string text, string sectionPattern)
    {
        var skills = new List<string>();

        // Primary: Extract bullet/line items under the section heading
        var bullets = ExtractBulletSection(text, sectionPattern);
        foreach (var bullet in bullets)
        {
            // Each bullet may contain multiple items separated by commas
            // e.g. "Deep expertise in C#, .NET Core/.NET 8, ASP.NET Core"
            var items = bullet.Split([',', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries);
            skills.AddRange(items.Select(s => s.Trim().TrimStart('-', '*', ' '))
                .Where(s => s.Length > 1 && s.Length < 80));
        }

        // Fallback: Same-line comma list after the keyword (e.g. "Skills: C#, Python, SQL")
        if (skills.Count < 3)
        {
            var sectionRegex = new Regex($@"(?:{sectionPattern})\s*:\s*([^\n]+)", RegexOptions.IgnoreCase);
            foreach (Match m in sectionRegex.Matches(text))
            {
                var line = m.Groups[1].Value.Trim();
                if (line.Length < 5) continue;
                var items = line.Split([',', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries);
                skills.AddRange(items.Select(s => s.Trim()).Where(s => s.Length > 1));
            }
        }

        return skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractBulletSection(string text, string sectionPattern)
    {
        var items = new List<string>();
        // Find the section header, then collect bullet/dash lines until next header or blank block
        var lines = text.Split('\n');
        bool inSection = false;
        int blankCount = 0;
        var headerRx = new Regex($@"^\s*(?:#{1,3}\s*)?(?:{sectionPattern})\s*:?\s*$",
            RegexOptions.IgnoreCase);
        var nextHeaderRx = new Regex(@"^\s*#{1,3}\s+\w", RegexOptions.IgnoreCase);
        var bulletRx = new Regex(@"^\s*[-•·*]\s+(.+)$");

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (headerRx.IsMatch(line)) { inSection = true; blankCount = 0; continue; }
            if (!inSection) continue;
            if (string.IsNullOrWhiteSpace(line)) { blankCount++; if (blankCount > 2) break; continue; }
            if (nextHeaderRx.IsMatch(line)) break;
            blankCount = 0;

            var m = bulletRx.Match(rawLine);
            if (m.Success)
                items.Add(m.Groups[1].Value.Trim());
            else if (line.Length > 20)   // plain sentence lines also count
                items.Add(line);

            if (items.Count >= 20) break;  // cap at 20 bullets
        }
        return items;
    }

}