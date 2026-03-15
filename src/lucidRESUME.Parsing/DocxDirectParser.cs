using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace lucidRESUME.Parsing;

/// <summary>
/// Parses .docx files directly via OpenXml without any HTTP call.
///
/// Heading detection strategy (in priority order):
///   1. Word built-in heading styles: "Heading 1" … "Heading 6"
///   2. Custom styles whose name contains "heading" (case-insensitive)
///   3. Bold + larger-than-body font size heuristic
///   4. ALL-CAPS short paragraphs (common in resume section labels)
///
/// The resulting markdown uses ## / ### prefixes so MarkdownSectionParser
/// can classify sections the same way it does with Docling output.
/// </summary>
public sealed class DocxDirectParser : IDocumentParser
{
    public IReadOnlyList<string> SupportedExtensions { get; } = [".docx"];

    public Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
            if (doc.MainDocumentPart?.Document?.Body is null)
                return Task.FromResult<ParsedDocument?>(null);

            var body = doc.MainDocumentPart.Document.Body;
            var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;

            var sb = new StringBuilder();       // markdown
            var plain = new StringBuilder();    // plain text
            var sections = new List<DocumentSection>();
            DocumentSection? currentSection = null;
            var bodyLines = new StringBuilder();

            foreach (var para in body.Elements<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();

                var text = GetParagraphText(para);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var headingLevel = DetectHeadingLevel(para, styles, text);

                if (headingLevel > 0)
                {
                    // Flush previous section
                    if (currentSection != null)
                    {
                        sections.Add(currentSection with { Body = bodyLines.ToString().Trim() });
                        bodyLines.Clear();
                    }
                    currentSection = new DocumentSection { Heading = text, Level = headingLevel };

                    var prefix = new string('#', headingLevel + 1); // h1→##, h2→###
                    sb.AppendLine($"{prefix} {text}");
                    plain.AppendLine(text);
                }
                else
                {
                    sb.AppendLine(text);
                    plain.AppendLine(text);
                    bodyLines.AppendLine(text);
                }
            }

            if (currentSection != null)
                sections.Add(currentSection with { Body = bodyLines.ToString().Trim() });

            return Task.FromResult<ParsedDocument?>(new ParsedDocument
            {
                Markdown = sb.ToString(),
                PlainText = plain.ToString(),
                Sections = sections,
                PageCount = 0, // OpenXml doesn't expose page count without rendering
                Confidence = sections.Count > 0 ? 0.85 : 0.5
            });
        }
        catch
        {
            return Task.FromResult<ParsedDocument?>(null);
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
            // Built-in heading styles
            var lower = styleId.ToLowerInvariant();
            if (lower == "heading1" || lower == "1") return 1;
            if (lower == "heading2" || lower == "2") return 2;
            if (lower == "heading3" || lower == "3") return 3;

            // Custom style names
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

        // Heuristic: bold run with no lowercase letters → section label
        var isBold = para.Descendants<Bold>().Any();
        if (isBold && text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
            return 2;

        // Heuristic: short all-caps line (common in plain-text-style resumes)
        if (text.Length < 50 && text.Length > 3
            && text == text.ToUpperInvariant()
            && text.Any(char.IsLetter)
            && !text.Contains(','))
            return 2;

        return 0;
    }
}
