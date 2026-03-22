namespace lucidRESUME.Parsing;

/// <summary>
/// Simple parser for .txt resume files. Detects sections by ALL-CAPS headings.
/// </summary>
public sealed class TxtParser : IDocumentParser
{
    public IReadOnlyList<string> SupportedExtensions { get; } = [".txt"];

    public async Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(filePath, ct);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n');
        var sections = new List<DocumentSection>();
        string? currentHeading = null;
        var bodyLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (IsHeading(trimmed))
            {
                if (currentHeading is not null)
                    sections.Add(new DocumentSection { Heading = currentHeading, Body = string.Join("\n", bodyLines).Trim(), Level = 1 });

                currentHeading = trimmed;
                bodyLines.Clear();
            }
            else
            {
                bodyLines.Add(line);
            }
        }

        if (currentHeading is not null)
            sections.Add(new DocumentSection { Heading = currentHeading, Body = string.Join("\n", bodyLines).Trim(), Level = 1 });

        var markdown = sections.Count > 0
            ? string.Join("\n\n", sections.Select(s => $"## {s.Heading}\n\n{s.Body}"))
            : text;

        return new ParsedDocument
        {
            Markdown = markdown,
            PlainText = text,
            Sections = sections,
            Confidence = sections.Count >= 2 ? 0.65 : 0.4,
            PageCount = 1
        };
    }

    private static bool IsHeading(string line) =>
        line.Length > 0 && line.Length <= 60
        && line == line.ToUpperInvariant()
        && line.Any(char.IsLetter);
}
