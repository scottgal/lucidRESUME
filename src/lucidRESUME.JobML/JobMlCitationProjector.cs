using System.Globalization;
using System.Text;

namespace lucidRESUME.JobML;

/// <summary>
/// Produces the deliberately lossy publication view of full JobML. It does not
/// infer claims or evidence. It only numbers already-linked external evidence,
/// adds those numbers to the cited prose, and renders a bibliography.
/// </summary>
public static class CJobMlProjector
{
    public const string ReferencesHeading = "## References";
    public const string SemanticPreamble =
        "cJobML 0.1: xref [n] in prose resolves to ref [n].";

    public static CJobMlProjection Project(JobMlFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var index = MarkdownEvidenceIndex.Create(file.Markdown);
        var references = new List<CJobMlReference>();
        var referenceNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var anchors = new List<CJobMlAnchor>();

        foreach (var claim in file.Data.Claims)
        {
            if (!string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase))
                continue;
            var proseEvidence = claim.Evidence.FirstOrDefault(IsProseEvidence);
            if (string.IsNullOrWhiteSpace(proseEvidence?.Ref) ||
                !index.TryGet(proseEvidence.Ref, out var passage))
                continue;

            var numbers = new List<int>();
            foreach (var evidence in claim.Evidence.Where(IsCompactReference))
            {
                var key = ReferenceKey(evidence);
                if (!referenceNumbers.TryGetValue(key, out var number))
                {
                    number = references.Count + 1;
                    referenceNumbers[key] = number;
                    var plainText = FormatReference(number, evidence);
                    references.Add(new CJobMlReference(number, evidence,
                        $"<a id=\"ref-{number}\"></a>{plainText}", plainText));
                }
                if (!numbers.Contains(number)) numbers.Add(number);
            }

            if (numbers.Count > 0)
                anchors.Add(new CJobMlAnchor(claim.Id, passage.Reference, passage.Text,
                    passage.SourceStart, passage.SourceLength, numbers));
        }

        var markdown = InsertCitations(file.Markdown, anchors);
        if (references.Count > 0)
        {
            var bibliography = string.Join("\n\n", references.Select(reference => reference.Markdown));
            var preamble = SemanticPreamble;
            if (Uri.TryCreate(file.Data.Document.CompleteLedger, UriKind.Absolute, out var completeLedger))
                preamble += $" Full JobML: <{completeLedger}>.";
            markdown = $"{markdown.TrimEnd()}\n\n{ReferencesHeading}\n\n{preamble}\n\n{bibliography}\n";
        }

        return new CJobMlProjection(markdown, anchors, references, file.Data.Document.CompleteLedger);
    }

    public static string Marker(IEnumerable<int> numbers) =>
        $"[{string.Join(", ", numbers.Order())}]";

    public static string MarkdownMarker(IEnumerable<int> numbers) => string.Join(", ",
        numbers.Order().Select(number => $"[[{number}]](#ref-{number})"));

    private static string InsertCitations(string markdown, IReadOnlyList<CJobMlAnchor> anchors)
    {
        var byPassage = anchors
            .GroupBy(anchor => (anchor.SourceStart, anchor.SourceLength))
            .Select(group => new
            {
                group.Key.SourceStart,
                group.Key.SourceLength,
                Numbers = group.SelectMany(anchor => anchor.ReferenceNumbers).Distinct().Order().ToList()
            })
            .OrderByDescending(item => item.SourceStart)
            .ToList();

        var builder = new StringBuilder(markdown);
        foreach (var citation in byPassage)
        {
            var insertAt = citation.SourceStart + citation.SourceLength;
            if (insertAt < 0 || insertAt > builder.Length) continue;
            builder.Insert(insertAt, $" {MarkdownMarker(citation.Numbers)}");
        }
        return builder.ToString();
    }

    private static bool IsProseEvidence(JobMlEvidence evidence) =>
        string.Equals(evidence.Type, "prose", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(evidence.Uri);

    private static bool IsCompactReference(JobMlEvidence evidence) =>
        !IsProseEvidence(evidence) &&
        (Uri.TryCreate(evidence.Uri, UriKind.Absolute, out _) ||
         string.Equals(evidence.Type, "qualification", StringComparison.OrdinalIgnoreCase));

    private static string ReferenceKey(JobMlEvidence evidence)
    {
        if (!string.IsNullOrWhiteSpace(evidence.Id)) return $"id:{evidence.Id.Trim()}";
        if (!string.IsNullOrWhiteSpace(evidence.Uri)) return $"uri:{NormalizeUri(evidence.Uri)}";
        return $"qualification:{evidence.Issuer}|{evidence.Qualification}";
    }

    private static string NormalizeUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped).TrimEnd('/')
            : uri.Trim();

    public static string FormatReference(int number, JobMlEvidence evidence)
    {
        var builder = new StringBuilder($"[{number}] ");
        if (evidence.Authors.Count > 0)
            builder.Append(string.Join(", ", evidence.Authors)).Append(". ");

        var title = evidence.Title;
        if (string.IsNullOrWhiteSpace(title)) title = evidence.Qualification;
        if (string.IsNullOrWhiteSpace(title) && Uri.TryCreate(evidence.Uri, UriKind.Absolute, out var uri))
            title = uri.Segments.LastOrDefault()?.Trim('/').Replace('-', ' ') ?? uri.Host;
        title = string.IsNullOrWhiteSpace(title) ? Humanize(evidence.Type) : title.Trim();

        builder.Append('“').Append(title).Append(".” ");

        if (!string.IsNullOrWhiteSpace(evidence.Publisher))
            builder.Append(evidence.Publisher.Trim()).Append(", ");
        else if (!string.IsNullOrWhiteSpace(evidence.Issuer))
            builder.Append(evidence.Issuer.Trim()).Append(", ");

        if (!string.IsNullOrWhiteSpace(evidence.Published))
            builder.Append(FormatDate(evidence.Published)).Append(". ");
        builder.Append('[').Append(Humanize(evidence.Type)).Append("] ");
        if (!string.IsNullOrWhiteSpace(evidence.Uri))
            builder.Append('<').Append(evidence.Uri).Append(">.");
        if (!string.IsNullOrWhiteSpace(evidence.Accessed))
            builder.Append(" Accessed: ").Append(FormatDate(evidence.Accessed)).Append('.');

        return builder.ToString().Trim();
    }

    private static string FormatDate(string value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : value.Trim();

    private static string Humanize(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').Replace('-', ' '));
}

public sealed record CJobMlProjection(
    string Markdown,
    IReadOnlyList<CJobMlAnchor> Anchors,
    IReadOnlyList<CJobMlReference> References,
    string? CompleteLedger);

public sealed record CJobMlAnchor(
    string ClaimId,
    string ProseReference,
    string ProseText,
    int SourceStart,
    int SourceLength,
    IReadOnlyList<int> ReferenceNumbers);

public sealed record CJobMlReference(
    int Number,
    JobMlEvidence Evidence,
    string Markdown,
    string PlainText);
