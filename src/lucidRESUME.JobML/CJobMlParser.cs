using System.Text.RegularExpressions;

namespace lucidRESUME.JobML;

/// <summary>
/// Deterministic one-pass parser for the compact cJobML publication projection.
/// This deliberately parses only the small contract emitted by <see cref="CJobMlProjector"/>.
/// </summary>
public static partial class CJobMlParser
{
    public static CJobMlDocument Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new CJobMlParseException("The cJobML document is empty.");

        var normalized = MarkdownEvidenceIndex.NormalizeNewlines(source);
        var headingMatches = ReferencesHeadingPattern().Matches(normalized);
        if (headingMatches.Count == 0)
            throw new CJobMlParseException("The document does not contain a cJobML References section.");
        var heading = headingMatches[^1].Index;

        var prose = normalized[..heading].TrimEnd();
        var referenceSection = normalized[heading..];
        if (!referenceSection.Contains(CJobMlProjector.SemanticPreamble, StringComparison.Ordinal))
            throw new CJobMlParseException("The References section does not contain the cJobML semantic contract.");

        var references = ReferencePattern().Matches(referenceSection)
            .Select(match => ParseReference(match))
            .ToList();
        var fullMatch = FullJobMlPattern().Match(referenceSection);
        Uri? fullJobMl = null;
        if (fullMatch.Success &&
            !Uri.TryCreate(fullMatch.Groups["uri"].Value, UriKind.Absolute, out fullJobMl))
            throw new CJobMlParseException("The Full JobML endpoint is not a valid absolute URI.");
        if (references.Count == 0 && fullJobMl is null)
            throw new CJobMlParseException("The cJobML References section contains neither numbered references nor a Full JobML endpoint.");
        if (references.Select(reference => reference.Number).Distinct().Count() != references.Count)
            throw new CJobMlParseException("The cJobML References section contains duplicate reference numbers.");
        var expectedNumbers = Enumerable.Range(1, references.Count).ToArray();
        if (!references.Select(reference => reference.Number).SequenceEqual(expectedNumbers))
            throw new CJobMlParseException("cJobML references must be consecutive and ordered from 1.");

        var xrefs = XrefPattern().Matches(prose)
            .Select(match => int.Parse(match.Groups["number"].Value))
            .ToList();
        var referenceNumbers = references.Select(reference => reference.Number).ToHashSet();
        var unresolved = xrefs.Where(number => !referenceNumbers.Contains(number)).Distinct().Order().ToList();
        if (unresolved.Count > 0)
            throw new CJobMlParseException($"Unresolved cJobML xref(s): {string.Join(", ", unresolved)}.");
        var citedNumbers = xrefs.Distinct().ToHashSet();
        var orphaned = referenceNumbers.Where(number => !citedNumbers.Contains(number)).Order().ToList();
        if (orphaned.Count > 0)
            throw new CJobMlParseException($"Uncited cJobML reference(s): {string.Join(", ", orphaned)}.");
        var firstAppearances = xrefs.Distinct().ToArray();
        if (!firstAppearances.SequenceEqual(expectedNumbers))
            throw new CJobMlParseException("cJobML xrefs must assign reference numbers in order of first appearance.");

        return new CJobMlDocument(prose, xrefs, references, fullJobMl);
    }

    public static bool TryParse(string source, out CJobMlDocument? document, out string? error)
    {
        try
        {
            document = Parse(source);
            error = null;
            return true;
        }
        catch (CJobMlParseException ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    private static CJobMlParsedReference ParseReference(Match match)
    {
        var number = int.Parse(match.Groups["number"].Value);
        var citation = match.Groups["citation"].Value.Trim();
        var uriMatch = UriPattern().Match(citation);
        var typeMatch = TypePattern().Match(citation);
        var titleMatch = TitlePattern().Match(citation);
        return new CJobMlParsedReference(
            number,
            citation,
            titleMatch.Success ? titleMatch.Groups["title"].Value : null,
            typeMatch.Success ? typeMatch.Groups["type"].Value : null,
            uriMatch.Success ? new Uri(uriMatch.Groups["uri"].Value, UriKind.Absolute) : null);
    }

    [GeneratedRegex(@"(?m)^<a id=""ref-(?<number>\d+)""></a>\[\k<number>\]\s+(?<citation>.+)$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"(?m)^## References[ \t]*$")]
    private static partial Regex ReferencesHeadingPattern();

    [GeneratedRegex(@"\[\[(?<number>\d+)\]\]\(#ref-\k<number>\)")]
    private static partial Regex XrefPattern();

    [GeneratedRegex(@"Full JobML:\s*<(?<uri>https?://[^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex FullJobMlPattern();

    [GeneratedRegex(@"<(?<uri>https?://[^>]+)>")]
    private static partial Regex UriPattern();

    [GeneratedRegex(@"\[(?<type>[^\]]+)\]\s*(?:<|$)")]
    private static partial Regex TypePattern();

    [GeneratedRegex("“(?<title>[^”]+?)(?:\\.)?”")]
    private static partial Regex TitlePattern();
}

public sealed class CJobMlParseException(string message) : Exception(message);

public sealed record CJobMlDocument(
    string Prose,
    IReadOnlyList<int> Xrefs,
    IReadOnlyList<CJobMlParsedReference> References,
    Uri? FullJobMl);

public sealed record CJobMlParsedReference(
    int Number,
    string Citation,
    string? Title,
    string? Type,
    Uri? Uri);
