using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.JobML;

namespace lucidRESUME.Ingestion.Web;

/// <summary>
/// Deterministically captures bibliographic metadata and a content fingerprint for
/// an article. It never turns article text into a resume claim; linking the returned
/// evidence to a claim is an explicit review action.
/// </summary>
public sealed class LinkedPostImporter(HttpClient http)
{
    private const int MaxDocumentBytes = 4 * 1024 * 1024;

    public async Task<LinkedPostDocument> ImportAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Linked posts must use an HTTP or HTTPS URL.", nameof(uri));

        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDocumentBytes)
            throw new InvalidDataException("The linked post exceeds the 4 MB import limit.");
        await response.Content.LoadIntoBufferAsync(MaxDocumentBytes, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);

        var effectiveUri = response.RequestMessage?.RequestUri ?? uri;
        var canonical = AbsoluteHttpUri(effectiveUri,
            document.QuerySelector("link[rel='canonical']")?.GetAttribute("href")) ?? effectiveUri;
        var title = Meta(document, "property", "og:title")
                    ?? Meta(document, "name", "twitter:title")
                    ?? document.QuerySelector("article h1, main h1, h1")?.TextContent.Trim()
                    ?? document.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidDataException("The linked post does not expose a title.");

        var author = Meta(document, "name", "author")
                     ?? Meta(document, "property", "article:author")
                     ?? JsonLdValue(document, "author", "name");
        var publisher = Meta(document, "property", "og:site_name")
                        ?? JsonLdValue(document, "publisher", "name")
                        ?? canonical.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        var published = ParseDate(
            Meta(document, "property", "article:published_time")
            ?? Meta(document, "name", "date")
            ?? JsonLdScalar(document, "datePublished"));

        foreach (var removable in document.QuerySelectorAll("script, style, nav, footer, noscript"))
            removable.Remove();
        var content = MarkdownEvidenceIndex.NormalizeText(
            document.QuerySelector("article")?.TextContent
            ?? document.QuerySelector("main")?.TextContent
            ?? document.Body?.TextContent
            ?? title);

        return new LinkedPostDocument(
            canonical,
            title,
            string.IsNullOrWhiteSpace(author) ? [] : [author],
            publisher,
            published,
            DateOnly.FromDateTime(DateTime.UtcNow),
            EvidenceLedgerBuilder.FastHash(content),
            content);
    }

    private static string? Meta(IDocument document, string attribute, string value) =>
        document.QuerySelector($"meta[{attribute}='{value}']")?.GetAttribute("content")?.Trim();

    private static Uri? AbsoluteHttpUri(Uri source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute
            : Uri.TryCreate(source, value, out var relative) ? relative : null;
        return candidate?.Scheme is "http" or "https" ? candidate : null;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? DateOnly.FromDateTime(parsed.UtcDateTime) : null;

    private static string? JsonLdScalar(IDocument document, string property)
    {
        foreach (var root in JsonLdRoots(document))
            if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static string? JsonLdValue(IDocument document, string property, string child)
    {
        foreach (var root in JsonLdRoots(document))
        {
            if (!root.TryGetProperty(property, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty(child, out var nested) && nested.ValueKind == JsonValueKind.String)
                return nested.GetString();
            if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty(child, out nested) && nested.ValueKind == JsonValueKind.String)
                        return nested.GetString();
        }
        return null;
    }

    private static IEnumerable<JsonElement> JsonLdRoots(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            JsonDocument? json = null;
            try { json = JsonDocument.Parse(script.TextContent); }
            catch (JsonException) { }
            if (json is null) continue;
            using (json)
            {
                var root = json.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    foreach (var item in root.EnumerateArray()) yield return item.Clone();
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("@graph", out var graph) &&
                         graph.ValueKind == JsonValueKind.Array)
                    foreach (var item in graph.EnumerateArray()) yield return item.Clone();
                else if (root.ValueKind == JsonValueKind.Object)
                    yield return root.Clone();
            }
        }
    }
}

public sealed record LinkedPostDocument(
    Uri CanonicalUri,
    string Title,
    IReadOnlyList<string> Authors,
    string? Publisher,
    DateOnly? PublishedOn,
    DateOnly AccessedOn,
    string ContentFingerprint,
    string Content)
{
    public JobMlEvidence ToJobMlEvidence() => new()
    {
        Id = $"article-{EvidenceLedgerBuilder.Slug(Title)}-{ContentFingerprint[^8..]}",
        Type = "article",
        Uri = CanonicalUri.ToString(),
        Title = Title,
        Authors = [.. Authors],
        Publisher = Publisher,
        Published = PublishedOn?.ToString("yyyy-MM-dd"),
        Accessed = AccessedOn.ToString("yyyy-MM-dd"),
        Fingerprint = new JobMlFingerprint { Text = ContentFingerprint }
    };
}
