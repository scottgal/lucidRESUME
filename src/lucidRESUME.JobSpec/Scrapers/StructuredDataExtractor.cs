using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSpec.Scrapers;

/// <summary>
/// Extracts structured job metadata from HTML:
///   - JSON-LD blocks with "@type": "JobPosting"
///   - OpenGraph meta tags (og:title, og:description) as fallback
/// </summary>
public sealed class StructuredDataExtractor
{
    private readonly ILogger<StructuredDataExtractor> _logger;

    public StructuredDataExtractor(ILogger<StructuredDataExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<StructuredJobData?> ExtractAsync(string html, CancellationToken ct = default)
    {
        var result = new StructuredJobData();
        var foundAny = false;

        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        // --- JSON-LD ---
        var scripts = document.QuerySelectorAll("script[type='application/ld+json']");
        foreach (var script in scripts)
        {
            try
            {
                var json = script.TextContent;
                if (string.IsNullOrWhiteSpace(json)) continue;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Handle @graph array
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("@graph", out var graph) &&
                    graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in graph.EnumerateArray())
                        if (TryMapJobPosting(item, result)) { foundAny = true; break; }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                        if (TryMapJobPosting(item, result)) { foundAny = true; break; }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryMapJobPosting(root, result)) foundAny = true;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON-LD block");
            }
        }

        // --- OpenGraph ---
        var ogTitle = GetMetaContent(document, "og:title");
        var ogDesc = GetMetaContent(document, "og:description");
        if (!string.IsNullOrWhiteSpace(ogTitle)) { result.OgTitle = ogTitle; foundAny = true; }
        if (!string.IsNullOrWhiteSpace(ogDesc)) { result.OgDescription = ogDesc; foundAny = true; }

        return foundAny ? result : null;
    }

    private bool TryMapJobPosting(JsonElement el, StructuredJobData result)
    {
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (!el.TryGetProperty("@type", out var typeProp)) return false;

        var type = typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? ""
            : "";

        // @type can be an array
        if (typeProp.ValueKind == JsonValueKind.Array)
            type = typeProp.EnumerateArray()
                       .FirstOrDefault(t => t.ValueKind == JsonValueKind.String)
                       .GetString() ?? "";

        if (!type.Contains("JobPosting", StringComparison.OrdinalIgnoreCase)) return false;

        // title
        if (el.TryGetProperty("title", out var titleEl))
            result.Title = titleEl.GetString();

        // hiringOrganization.name
        if (el.TryGetProperty("hiringOrganization", out var org) &&
            org.ValueKind == JsonValueKind.Object &&
            org.TryGetProperty("name", out var orgName))
            result.Company = orgName.GetString();

        // jobLocation.address.addressLocality
        if (el.TryGetProperty("jobLocation", out var loc))
        {
            // jobLocation can be array or object
            var locEl = loc.ValueKind == JsonValueKind.Array
                ? loc.EnumerateArray().FirstOrDefault()
                : loc;

            if (locEl.ValueKind == JsonValueKind.Object &&
                locEl.TryGetProperty("address", out var addr) &&
                addr.ValueKind == JsonValueKind.Object &&
                addr.TryGetProperty("addressLocality", out var locality))
                result.Location = locality.GetString();
        }

        // jobLocationType - TELECOMMUTE = remote
        if (el.TryGetProperty("jobLocationType", out var locType))
        {
            var locTypeStr = locType.GetString() ?? "";
            if (locTypeStr.Contains("TELECOMMUTE", StringComparison.OrdinalIgnoreCase))
                result.IsRemote = true;
        }

        // employmentType
        if (el.TryGetProperty("employmentType", out var empType))
            result.EmploymentType = empType.GetString();

        // baseSalary
        if (el.TryGetProperty("baseSalary", out var salary) &&
            salary.ValueKind == JsonValueKind.Object)
        {
            if (salary.TryGetProperty("currency", out var cur))
                result.SalaryCurrency = cur.GetString();

            if (salary.TryGetProperty("value", out var val))
            {
                // value can be MonetaryAmount with minValue/maxValue or a direct number
                if (val.ValueKind == JsonValueKind.Object)
                {
                    if (val.TryGetProperty("minValue", out var minVal) &&
                        minVal.TryGetDecimal(out var min)) result.SalaryMin = min;
                    if (val.TryGetProperty("maxValue", out var maxVal) &&
                        maxVal.TryGetDecimal(out var max)) result.SalaryMax = max;
                }
                else if (val.TryGetDecimal(out var single))
                {
                    result.SalaryMin = single;
                    result.SalaryMax = single;
                }
            }
        }

        _logger.LogDebug("JSON-LD JobPosting found: title={Title}, company={Company}", result.Title, result.Company);
        return true;
    }

    private static string? GetMetaContent(IDocument document, string property)
    {
        var el = document.QuerySelector($"meta[property='{property}']")
                 ?? document.QuerySelector($"meta[name='{property}']");
        return el?.GetAttribute("content");
    }
}