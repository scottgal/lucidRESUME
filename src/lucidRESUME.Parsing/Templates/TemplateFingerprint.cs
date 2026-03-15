using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// A lightweight structural signature of a Word document.
/// Two documents from the same template will produce the same (or very similar) fingerprint.
///
/// Fingerprint components:
///   - Ordered list of unique paragraph style names (captures template structure)
///   - Number of heading levels used
///   - Whether content controls (SDTs) are present
///   - Custom document property names (some templates set these)
/// </summary>
public sealed class TemplateFingerprint
{
    /// <summary>Stable hex hash of the style sequence — use for fast lookup.</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    /// <summary>Ordered unique paragraph style names seen in the document.</summary>
    [JsonPropertyName("styleSequence")]
    public IReadOnlyList<string> StyleSequence { get; init; } = [];

    /// <summary>Heading level counts, e.g. [3, 7, 0] = 3×H1, 7×H2, 0×H3.</summary>
    [JsonPropertyName("headingCounts")]
    public IReadOnlyList<int> HeadingCounts { get; init; } = [];

    /// <summary>Whether the document contains Word content controls (SDTs).</summary>
    [JsonPropertyName("hasContentControls")]
    public bool HasContentControls { get; init; }

    /// <summary>Custom document property names, sorted.</summary>
    [JsonPropertyName("customProperties")]
    public IReadOnlyList<string> CustomProperties { get; init; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static TemplateFingerprint? FromFile(string filePath)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
            return FromDocument(doc);
        }
        catch { return null; }
    }

    public static TemplateFingerprint FromDocument(WordprocessingDocument doc)
    {
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return Empty();

        // Collect ordered unique style IDs
        var styleSeq = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headingCounts = new int[6];

        foreach (var para in body.Elements<Paragraph>())
        {
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";
            if (seen.Add(styleId)) styleSeq.Add(styleId);

            // Count headings
            for (var i = 1; i <= 6; i++)
            {
                if (styleId.Equals($"Heading{i}", StringComparison.OrdinalIgnoreCase) ||
                    styleId.Equals(i.ToString()))
                    headingCounts[i - 1]++;
            }
        }

        // Content controls
        var hasSDTs = body.Descendants<SdtElement>().Any();

        // Custom document properties
        var customProps = doc.CustomFilePropertiesPart?.Properties?
            .Elements()
            .Select(e => e.GetAttribute("name", "").Value ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n)
            .ToList() ?? [];

        var hash = ComputeHash(styleSeq, headingCounts, hasSDTs);

        return new TemplateFingerprint
        {
            Hash = hash,
            StyleSequence = styleSeq,
            HeadingCounts = headingCounts,
            HasContentControls = hasSDTs,
            CustomProperties = customProps
        };
    }

    // ── Similarity ────────────────────────────────────────────────────────────

    /// <summary>
    /// Jaccard similarity of style sequences. Returns 0..1.
    /// 1.0 = identical template, 0.0 = no shared styles.
    /// </summary>
    public double SimilarityTo(TemplateFingerprint other)
    {
        if (Hash == other.Hash) return 1.0;
        var a = new HashSet<string>(StyleSequence, StringComparer.OrdinalIgnoreCase);
        var b = new HashSet<string>(other.StyleSequence, StringComparer.OrdinalIgnoreCase);
        var intersection = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string ComputeHash(List<string> styles, int[] headings, bool hasSdts)
    {
        var input = string.Join("|", styles) + ";" + string.Join(",", headings) + ";" + hasSdts;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }

    private static TemplateFingerprint Empty() => new() { Hash = "empty" };
}
