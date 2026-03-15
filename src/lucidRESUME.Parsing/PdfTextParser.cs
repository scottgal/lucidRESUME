using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace lucidRESUME.Parsing;

/// <summary>
/// Extracts text from PDF files using PdfPig (no HTTP, no OCR).
/// Works well for text-layer PDFs (most modern CV exports).
/// Falls back gracefully for scanned/image PDFs (returns low confidence).
///
/// Heading detection: uses font size relative to the document's body font size.
/// Lines whose font size is ≥ 1.3× the median body size are treated as headings.
/// </summary>
public sealed class PdfTextParser : IDocumentParser
{
    public IReadOnlyList<string> SupportedExtensions { get; } = [".pdf"];

    public Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var pdf = PdfDocument.Open(filePath);

            var allLines = new List<(string text, double fontSize, int page)>();

            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                var pageLines = GroupWordsIntoLines(page);
                allLines.AddRange(pageLines.Select(l => (l.text, l.fontSize, page.Number)));
            }

            if (allLines.Count == 0)
                return Task.FromResult<ParsedDocument?>(null);

            var bodyFontSize = Median(allLines.Select(l => l.fontSize).ToList());
            var headingThreshold = bodyFontSize * 1.25;

            var sb = new StringBuilder();
            var plain = new StringBuilder();
            var sections = new List<DocumentSection>();
            DocumentSection? current = null;
            var bodyBuf = new StringBuilder();

            foreach (var (text, fontSize, _) in allLines)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var isHeading = fontSize >= headingThreshold
                    || (text.Length < 50 && text == text.ToUpperInvariant() && text.Any(char.IsLetter));

                if (isHeading)
                {
                    if (current != null)
                        sections.Add(current with { Body = bodyBuf.ToString().Trim() });
                    bodyBuf.Clear();
                    current = new DocumentSection { Heading = text, Level = fontSize >= bodyFontSize * 1.5 ? 1 : 2 };

                    var prefix = current.Level == 1 ? "##" : "###";
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

            if (current != null)
                sections.Add(current with { Body = bodyBuf.ToString().Trim() });

            var totalChars = allLines.Sum(l => l.text.Length);
            var confidence = totalChars > 200 ? 0.75 : 0.3; // low confidence = likely scanned

            return Task.FromResult<ParsedDocument?>(new ParsedDocument
            {
                Markdown = sb.ToString(),
                PlainText = plain.ToString(),
                Sections = sections,
                PageCount = pdf.NumberOfPages,
                Confidence = confidence
            });
        }
        catch
        {
            return Task.FromResult<ParsedDocument?>(null);
        }
    }

    // ── Word grouping ────────────────────────────────────────────────────────

    private static List<(string text, double fontSize)> GroupWordsIntoLines(Page page)
    {
        // Group words by their baseline Y position (within 2pt tolerance)
        var groups = new Dictionary<int, List<Word>>();
        foreach (var word in page.GetWords())
        {
            var bucket = (int)Math.Round(word.BoundingBox.Bottom / 2.0) * 2;
            if (!groups.TryGetValue(bucket, out var list))
                groups[bucket] = list = [];
            list.Add(word);
        }

        return groups
            .OrderByDescending(g => g.Key) // top of page first
            .Select(g =>
            {
                var words = g.Value.OrderBy(w => w.BoundingBox.Left).ToList();
                var text = string.Join(" ", words.Select(w => w.Text)).Trim();
                var fontSize = words
                    .SelectMany(w => w.Letters)
                    .Select(l => l.FontSize)
                    .DefaultIfEmpty(12)
                    .Average();
                return (text, fontSize);
            })
            .Where(l => !string.IsNullOrWhiteSpace(l.text))
            .ToList();
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 12;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2.0 : values[mid];
    }
}
