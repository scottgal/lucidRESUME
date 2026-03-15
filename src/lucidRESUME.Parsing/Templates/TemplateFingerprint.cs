using System.IO.Hashing;
using System.Text;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// A structural signature of a Word document's *template definition* — not its content.
///
/// Two documents produced from the same .dotx template will produce identical (or near-
/// identical) fingerprints regardless of how much content differs between them.
///
/// Stable components used for fingerprinting:
///   1. Sorted set of defined style names  (from styles.xml — template-level)
///   2. Default body font name + size      (Normal style)
///   3. Page margins                       (section properties)
///   4. Attached template name             (from app.xml <Template>)
///   5. Whether the doc uses content controls (SDTs)
///
/// Notably NOT used: which styles appear on which paragraphs (content-dependent),
/// paragraph counts, headings used, author metadata.
/// </summary>
public sealed class TemplateFingerprint
{
    /// <summary>
    /// XxHash64 of the stable template components.
    /// Same value for any document from the same template.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    /// <summary>Sorted, distinct style names defined in the document's style gallery.</summary>
    [JsonPropertyName("definedStyles")]
    public IReadOnlyList<string> DefinedStyles { get; init; } = [];

    /// <summary>Default body font (from Normal style's rFonts, or document defaults).</summary>
    [JsonPropertyName("defaultFont")]
    public string DefaultFont { get; init; } = string.Empty;

    /// <summary>Default font size in half-points (e.g. 24 = 12pt).</summary>
    [JsonPropertyName("defaultFontSizeHp")]
    public int DefaultFontSizeHp { get; init; }

    /// <summary>Page margins as "top,right,bottom,left" in twentieths of a point.</summary>
    [JsonPropertyName("pageMargins")]
    public string PageMargins { get; init; } = string.Empty;

    /// <summary>
    /// Template file name from app.xml &lt;Template&gt; (e.g. "ScottCV.dotx", "Normal.dotm").
    /// Empty string when not set.
    /// </summary>
    [JsonPropertyName("attachedTemplate")]
    public string AttachedTemplate { get; init; } = string.Empty;

    /// <summary>Whether the document uses Word content controls (SDTs).</summary>
    [JsonPropertyName("hasContentControls")]
    public bool HasContentControls { get; init; }

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
        // 1. Defined style names (sorted, lowercased for case-insensitive matching)
        var definedStyles = doc.MainDocumentPart?.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .Select(s => s.StyleName?.Val?.Value ?? s.StyleId?.Value ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n.ToLowerInvariant())
            .Distinct()
            .OrderBy(n => n)
            .ToList() ?? [];

        // 2. Default font + size from Normal style (or document defaults)
        var (defaultFont, defaultSizeHp) = ExtractDefaultFont(doc);

        // 3. First section's page margins (top,right,bottom,left in twips)
        var margins = ExtractPageMargins(doc);

        // 4. Attached template name from app.xml
        var attachedTemplate = ExtractTemplateName(doc);

        // 5. Content controls
        var hasSDTs = doc.MainDocumentPart?.Document?.Body?.Descendants<SdtElement>().Any() ?? false;

        var hash = ComputeHash(definedStyles, defaultFont, defaultSizeHp, margins, attachedTemplate);

        return new TemplateFingerprint
        {
            Hash = hash,
            DefinedStyles = definedStyles,
            DefaultFont = defaultFont,
            DefaultFontSizeHp = defaultSizeHp,
            PageMargins = margins,
            AttachedTemplate = attachedTemplate,
            HasContentControls = hasSDTs
        };
    }

    // ── Similarity ────────────────────────────────────────────────────────────

    /// <summary>
    /// Jaccard similarity over the *defined style sets*.
    /// Returns 0..1. Documents from the same template will score ≥ 0.95.
    /// Completely unrelated templates will score near 0.
    /// </summary>
    public double SimilarityTo(TemplateFingerprint other)
    {
        // Fast path: identical hash
        if (Hash == other.Hash) return 1.0;

        // If both have an attached template name that matches, treat as very high similarity
        if (!string.IsNullOrEmpty(AttachedTemplate)
            && string.Equals(AttachedTemplate, other.AttachedTemplate, StringComparison.OrdinalIgnoreCase))
            return 0.97;

        // Jaccard over defined style sets
        if (DefinedStyles.Count == 0 && other.DefinedStyles.Count == 0) return 0;
        var a = new HashSet<string>(DefinedStyles);
        var b = new HashSet<string>(other.DefinedStyles);
        var intersection = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - intersection;
        var jaccard = union == 0 ? 0.0 : (double)intersection / union;

        // Tiebreak: matching page margins adds a small bonus
        if (PageMargins == other.PageMargins && PageMargins.Length > 0)
            jaccard = Math.Min(1.0, jaccard + 0.05);

        return jaccard;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static (string font, int sizeHp) ExtractDefaultFont(WordprocessingDocument doc)
    {
        var normalStyle = doc.MainDocumentPart?.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .FirstOrDefault(s => s.StyleId?.Value is "Normal" or "DefaultParagraphFont");

        var font = normalStyle?.StyleRunProperties?.RunFonts?.Ascii?.Value
                   ?? doc.MainDocumentPart?.DocumentSettingsPart?.Settings?
                       .Elements<DefaultTabStop>().FirstOrDefault()?.Val?.Value.ToString()
                   ?? "";

        var sizeStr = normalStyle?.StyleRunProperties?.FontSize?.Val?.Value;
        var size = int.TryParse(sizeStr, out var s) ? s : 24; // 24 half-pts = 12pt default

        return (font, size);
    }

    private static string ExtractPageMargins(WordprocessingDocument doc)
    {
        var pgMar = doc.MainDocumentPart?.Document?.Body?
            .Elements<SectionProperties>().FirstOrDefault()?
            .Elements<PageMargin>().FirstOrDefault();

        if (pgMar is null) return "";
        return $"{pgMar.Top},{pgMar.Right},{pgMar.Bottom},{pgMar.Left}";
    }

    private static string ExtractTemplateName(WordprocessingDocument doc)
    {
        // app.xml stores the originating template name
        try
        {
            var app = doc.ExtendedFilePropertiesPart?.Properties;
            return app?.Template?.Text?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static string ComputeHash(
        List<string> styles, string font, int sizeHp, string margins, string templateName)
    {
        // Combine all stable components into a single byte span and hash with XxHash64
        var input = string.Join("|", styles) + $";{font};{sizeHp};{margins};{templateName}";
        var hash = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(input));
        return hash.ToString("x16");
    }
}
