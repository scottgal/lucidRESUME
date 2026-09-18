using System.Text;
using System.Text.RegularExpressions;

namespace lucidRESUME.JobML;

public sealed record ProsePassage(
    string Reference,
    string Text,
    string Fingerprint,
    string? HeadingId,
    string? Heading,
    string? Section,
    int ParagraphNumber,
    int SourceStart,
    int SourceLength);

public sealed class MarkdownEvidenceIndex
{
    private static readonly Regex HeadingPattern = new(
        @"^(?<marks>#{1,6})\s+(?<title>.*?)(?:\s+\{#(?<id>[A-Za-z][A-Za-z0-9_.-]*)\})?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex ExplicitParagraphPattern = new(
        "^\\s*<p\\s+id=[\\\"'](?<id>[A-Za-z][A-Za-z0-9_.-]*)[\\\"']\\s*>\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownSyntaxPattern = new(
        @"(?:[*_`~]|\[(.*?)\]\([^)]*\))",
        RegexOptions.Compiled);

    private readonly Dictionary<string, ProsePassage> _byReference;
    private readonly HashSet<string> _ambiguousReferences;

    private MarkdownEvidenceIndex(IReadOnlyList<ProsePassage> passages)
    {
        Passages = passages;
        var referenceGroups = passages
            .GroupBy(p => p.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _byReference = referenceGroups
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        _ambiguousReferences = referenceGroups
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProsePassage> Passages { get; }

    public bool TryGet(string reference, out ProsePassage passage)
    {
        var normalized = NormalizeReference(reference);
        if (_ambiguousReferences.Contains(normalized))
        {
            passage = null!;
            return false;
        }
        return _byReference.TryGetValue(normalized, out passage!);
    }

    public bool IsAmbiguous(string reference) =>
        _ambiguousReferences.Contains(NormalizeReference(reference));

    public IReadOnlyList<ProsePassage> FindByFingerprint(string fingerprint) => Passages
        .Where(p => MatchesFingerprint(p.Text, fingerprint))
        .ToList();

    public IReadOnlyList<ProsePassage> FindByQuote(JobMlTextSelector selector)
    {
        var exact = NormalizeText(selector.Exact);
        if (exact.Length == 0) return [];
        var candidates = Passages.Where(p => string.Equals(p.Text, exact, StringComparison.Ordinal)).ToList();
        if (candidates.Count <= 1) return candidates;

        return candidates.Where(candidate =>
        {
            var index = IndexOf(Passages, candidate);
            var prefixMatches = string.IsNullOrWhiteSpace(selector.Prefix) ||
                (index > 0 && Passages[index - 1].Text.EndsWith(NormalizeText(selector.Prefix), StringComparison.Ordinal));
            var suffixMatches = string.IsNullOrWhiteSpace(selector.Suffix) ||
                (index < Passages.Count - 1 && Passages[index + 1].Text.StartsWith(NormalizeText(selector.Suffix), StringComparison.Ordinal));
            return prefixMatches && suffixMatches;
        }).ToList();
    }

    private static int IndexOf(IReadOnlyList<ProsePassage> passages, ProsePassage passage)
    {
        for (var i = 0; i < passages.Count; i++)
            if (ReferenceEquals(passages[i], passage) || passages[i] == passage) return i;
        return -1;
    }

    public static MarkdownEvidenceIndex Create(string markdown)
    {
        var passages = new List<ProsePassage>();
        var paragraph = new StringBuilder();
        var normalizedMarkdown = NormalizeNewlines(markdown);
        var lines = normalizedMarkdown.Split('\n');
        string? headingId = null;
        string? heading = null;
        string? section = null;
        string? explicitParagraphId = null;
        var paragraphNumber = 0;
        var inFence = false;
        var paragraphStart = -1;
        var paragraphEnd = -1;

        void Flush()
        {
            var text = NormalizeText(paragraph.ToString());
            paragraph.Clear();
            if (text.Length == 0) return;

            paragraphNumber++;
            var reference = explicitParagraphId is not null
                ? $"#{explicitParagraphId}"
                : headingId is not null
                    ? $"#{headingId}:p{paragraphNumber}"
                    : $"#document:p{passages.Count + 1}";
            passages.Add(new ProsePassage(
                reference,
                text,
                Fingerprint(text),
                headingId,
                heading,
                section,
                paragraphNumber,
                paragraphStart,
                Math.Max(0, paragraphEnd - paragraphStart)));
            explicitParagraphId = null;
            paragraphStart = -1;
            paragraphEnd = -1;
        }

        var sourceOffset = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var lineStart = sourceOffset;
            sourceOffset += rawLine.Length + (lineIndex < lines.Length - 1 ? 1 : 0);
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            var headingMatch = HeadingPattern.Match(line);
            if (headingMatch.Success)
            {
                Flush();
                var level = headingMatch.Groups["marks"].Value.Length;
                heading = headingMatch.Groups["title"].Value.Trim();
                var parsedId = headingMatch.Groups["id"].Value;
                headingId = parsedId.Length > 0 ? parsedId : null;
                paragraphNumber = 0;
                if (level == 2) section = heading;
                continue;
            }

            var explicitMatch = ExplicitParagraphPattern.Match(line);
            if (explicitMatch.Success)
            {
                Flush();
                explicitParagraphId = explicitMatch.Groups["id"].Value;
                continue;
            }
            if (line.Trim().Equals("</p>", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
            {
                Flush();
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            if (paragraphStart < 0) paragraphStart = lineStart;
            paragraphEnd = lineStart + rawLine.Length;
            paragraph.Append(trimmed);
        }
        Flush();
        return new MarkdownEvidenceIndex(passages);
    }

    public static string Fingerprint(string text)
    {
        // FNV-1a is intentionally non-cryptographic: this runs continuously while typing
        // and detects accidental prose drift, not adversarial tampering. The stable ref and
        // text quote selector provide the other two parts of the evidence identity.
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var value in Encoding.UTF8.GetBytes(NormalizeText(text)))
        {
            hash ^= value;
            hash *= prime;
        }
        return $"fnv1a64:{hash:x16}";
    }

    public static bool MatchesFingerprint(string text, string expected) =>
        string.Equals(Fingerprint(text), expected, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeText(string text)
    {
        var withoutLinks = MarkdownSyntaxPattern.Replace(text, "$1");
        return Regex.Replace(withoutLinks, @"\s+", " ").Trim();
    }

    internal static string NormalizeReference(string reference) =>
        reference.StartsWith('#') ? reference : $"#{reference}";

    internal static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
