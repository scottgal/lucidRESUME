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
        var allWords = page.GetWords().ToList();
        if (allWords.Count == 0) return [];

        // Detect if this is a multi-column layout by looking at word X-position distribution
        var xPositions = allWords.Select(w => w.BoundingBox.Left).OrderBy(x => x).ToList();
        var pageWidth = allWords.Max(w => w.BoundingBox.Right) - allWords.Min(w => w.BoundingBox.Left);
        var columnBoundary = DetectColumnBoundary(allWords, pageWidth);

        // Split words into columns
        List<List<Word>> columns;
        if (columnBoundary.HasValue)
        {
            columns =
            [
                allWords.Where(w => w.BoundingBox.Left < columnBoundary.Value).ToList(),
                allWords.Where(w => w.BoundingBox.Left >= columnBoundary.Value).ToList()
            ];
        }
        else
        {
            columns = [allWords];
        }

        // Process each column independently: group by Y, sort top-to-bottom
        var result = new List<(string text, double fontSize)>();
        foreach (var colWords in columns)
        {
            if (colWords.Count == 0) continue;
            var lines = GroupColumnIntoLines(colWords);
            result.AddRange(lines);
        }

        return result;
    }

    /// <summary>
    /// Detects the X-position boundary between two columns, if present.
    /// Uses gap analysis: finds the largest horizontal gap in word positions.
    /// Returns null if the page is single-column.
    /// </summary>
    private static double? DetectColumnBoundary(List<Word> words, double pageWidth)
    {
        if (words.Count < 10 || pageWidth < 200) return null;

        // Collect all word right-edges and left-edges, find the biggest gap
        var edges = words
            .Select(w => (left: w.BoundingBox.Left, right: w.BoundingBox.Right))
            .OrderBy(e => e.left)
            .ToList();

        // Group into Y-rows and check if rows consistently have large internal gaps
        var yGroups = new Dictionary<int, List<(double left, double right)>>();
        foreach (var w in words)
        {
            var bucket = (int)Math.Round(w.BoundingBox.Bottom / 3.0) * 3;
            if (!yGroups.TryGetValue(bucket, out var list))
                yGroups[bucket] = list = [];
            list.Add((w.BoundingBox.Left, w.BoundingBox.Right));
        }

        // For each row, find the max internal gap
        var rowGaps = new List<double>();
        foreach (var row in yGroups.Values.Where(r => r.Count >= 2))
        {
            var sorted = row.OrderBy(w => w.left).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                var gap = sorted[i].left - sorted[i - 1].right;
                if (gap > 30) rowGaps.Add((sorted[i].left + sorted[i - 1].right) / 2);
            }
        }

        if (rowGaps.Count < 3) return null; // not enough evidence of two columns

        // Find the most common gap position (column boundary)
        var bucketedGaps = rowGaps.GroupBy(g => Math.Round(g / 10) * 10)
            .OrderByDescending(g => g.Count())
            .First();

        // Only declare two columns if >30% of rows have the gap
        if (bucketedGaps.Count() < yGroups.Count * 0.3) return null;

        return bucketedGaps.Average();
    }

    private static List<(string text, double fontSize)> GroupColumnIntoLines(List<Word> words)
    {
        var groups = new Dictionary<int, List<Word>>();
        foreach (var word in words)
        {
            var bucket = (int)Math.Round(word.BoundingBox.Bottom / 2.0) * 2;
            if (!groups.TryGetValue(bucket, out var list))
                groups[bucket] = list = [];
            list.Add(word);
        }

        return groups
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var lineWords = g.Value.OrderBy(w => w.BoundingBox.Left).ToList();
                var text = string.Join(" ", lineWords.Select(w => w.Text)).Trim();
                var fontSize = lineWords
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
