using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using lucidRESUME.Parsing.Templates;

namespace lucidRESUME.Parsing;

/// <summary>
/// Parses .docx files directly via OpenXml without any HTTP call.
///
/// Heading detection strategy (in priority order):
///   1. Word built-in heading styles: "Heading 1" … "Heading 6"
///   2. Custom styles whose name contains "heading" (case-insensitive)
///   3. Bold + ALL-CAPS short paragraphs (common in resume section labels)
///
/// Template fingerprinting: if a <see cref="TemplateRegistry"/> is provided,
/// the document is matched against known templates. A match boosts confidence
/// and can unlock template-specific field mappings in future.
/// </summary>
public sealed class DocxDirectParser : IDocumentParser
{
    private readonly TemplateRegistry? _registry;

    public DocxDirectParser(TemplateRegistry? registry = null)
    {
        _registry = registry;
    }

    public IReadOnlyList<string> SupportedExtensions { get; } = [".docx"];

    public async Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
            if (doc.MainDocumentPart?.Document?.Body is null)
                return null;

            var body = doc.MainDocumentPart.Document.Body;
            var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;

            // ── Template fingerprint lookup ───────────────────────────────
            var fingerprint = TemplateFingerprint.FromDocument(doc);
            KnownTemplate? matchedTemplate = null;
            if (_registry is not null)
                matchedTemplate = await _registry.FindMatchAsync(fingerprint, ct);

            // ── Content extraction ────────────────────────────────────────
            var sb = new StringBuilder();
            var plain = new StringBuilder();
            var sections = new List<DocumentSection>();
            DocumentSection? currentSection = null;
            var bodyBuf = new StringBuilder();

            foreach (var para in body.Elements<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();

                var text = GetParagraphText(para);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var headingLevel = DetectHeadingLevel(para, styles, text);

                if (headingLevel > 0)
                {
                    if (currentSection != null)
                        sections.Add(currentSection with { Body = bodyBuf.ToString().Trim() });
                    bodyBuf.Clear();
                    currentSection = new DocumentSection { Heading = text, Level = headingLevel };

                    var prefix = new string('#', headingLevel + 1); // h1→##, h2→###
                    sb.AppendLine($"{prefix} {text}");
                    plain.AppendLine(text);
                }
                else
                {
                    sb.AppendLine(text);
                    plain.AppendLine(text);
                    bodyBuf.AppendLine(text);
                }
            }

            if (currentSection != null)
                sections.Add(currentSection with { Body = bodyBuf.ToString().Trim() });

            // ── Confidence ────────────────────────────────────────────────
            var baseConfidence = sections.Count > 0 ? 0.85 : 0.5;
            var confidence = Math.Min(1.0, baseConfidence + (matchedTemplate?.ConfidenceBoost ?? 0));

            return new ParsedDocument
            {
                Markdown = sb.ToString(),
                PlainText = plain.ToString(),
                Sections = sections,
                PageCount = 0,
                Confidence = confidence,
                TemplateName = matchedTemplate?.Name
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Text extraction ──────────────────────────────────────────────────────

    private static string GetParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var run in para.Elements<Run>())
            sb.Append(run.InnerText);
        return sb.ToString().Trim();
    }

    // ── Heading detection ────────────────────────────────────────────────────

    private static int DetectHeadingLevel(Paragraph para, Styles? styles, string text)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId != null)
        {
            var lower = styleId.ToLowerInvariant();
            if (lower == "heading1" || lower == "1") return 1;
            if (lower == "heading2" || lower == "2") return 2;
            if (lower == "heading3" || lower == "3") return 3;

            if (styles != null)
            {
                var styleName = styles.Elements<Style>()
                    .FirstOrDefault(s => s.StyleId?.Value == styleId)
                    ?.StyleName?.Val?.Value ?? "";
                var sn = styleName.ToLowerInvariant();
                if (sn.Contains("heading") || sn.Contains("title") || sn.Contains("section"))
                    return sn.Contains("2") || sn.Contains("sub") ? 2 : 1;
            }
        }

        var isBold = para.Descendants<Bold>().Any();
        if (isBold && text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
            return 2;

        if (text.Length < 50 && text.Length > 3
            && text == text.ToUpperInvariant()
            && text.Any(char.IsLetter)
            && !text.Contains(','))
            return 2;

        return 0;
    }
}
