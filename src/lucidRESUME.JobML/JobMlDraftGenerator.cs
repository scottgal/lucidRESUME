using System.Text.RegularExpressions;

namespace lucidRESUME.JobML;

public sealed class JobMlDraftGenerator
{
    private static readonly Regex HeadingPattern = new(
        @"^(?<marks>#{2,6})\s+(?<title>.*?)(?:\s+\{#(?<id>[A-Za-z][A-Za-z0-9_.-]*)\})?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex FirstHeadingPattern = new(
        @"^#\s+(?<title>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static JobMlFile Generate(string markdown, string language = "en-GB")
    {
        var anchoredMarkdown = AddStableHeadingAnchors(markdown);
        var index = MarkdownEvidenceIndex.Create(anchoredMarkdown);
        var documentName = FirstHeadingPattern.Match(anchoredMarkdown).Groups["title"].Value;
        var documentId = Slug(string.IsNullOrWhiteSpace(documentName) ? "resume" : documentName);
        var root = new JobMlRoot
        {
            Document = new JobMlDocumentMetadata { Id = documentId, Language = language }
        };

        foreach (var headingGroup in index.Passages
                     .Where(p => p.HeadingId is not null)
                     .GroupBy(p => p.HeadingId!, StringComparer.OrdinalIgnoreCase))
        {
            var first = headingGroup.First();
            var entity = new JobMlEntity
            {
                Id = first.HeadingId!,
                Type = InferEntityType(first.Section),
                Name = first.Heading ?? first.HeadingId!,
                Source = $"#{first.HeadingId}"
            };
            root.Entities.Add(entity);

            foreach (var passage in headingGroup.Where(p => p.Text.Length >= 20))
            {
                root.Claims.Add(new JobMlClaim
                {
                    Id = $"{entity.Id}-claim-{passage.ParagraphNumber}",
                    Subject = entity.Id,
                    Statement = passage.Text,
                    Evidence =
                    [
                        new JobMlEvidence
                        {
                            Type = "prose",
                            Ref = passage.Reference,
                            Fingerprint = new JobMlFingerprint { Text = passage.Fingerprint },
                            Selector = new JobMlTextSelector
                            {
                                Exact = passage.Text,
                                Prefix = PreviousContext(index.Passages, passage),
                                Suffix = NextContext(index.Passages, passage)
                            }
                        }
                    ],
                    Origin = "derived",
                    Review = "required"
                });
            }
        }

        return new JobMlFile(anchoredMarkdown, root);
    }

    public static string AddStableHeadingAnchors(string markdown)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return HeadingPattern.Replace(MarkdownEvidenceIndex.NormalizeNewlines(markdown), match =>
        {
            var existing = match.Groups["id"].Value;
            if (existing.Length > 0)
            {
                used.Add(existing);
                return match.Value;
            }

            var baseId = Slug(match.Groups["title"].Value);
            var id = baseId;
            for (var suffix = 2; !used.Add(id); suffix++) id = $"{baseId}-{suffix}";
            return $"{match.Groups["marks"].Value} {match.Groups["title"].Value.Trim()} {{#{id}}}";
        });
    }

    private static string InferEntityType(string? section)
    {
        var value = section?.ToLowerInvariant() ?? "";
        if (value.Contains("project")) return "project";
        if (value.Contains("education")) return "education";
        if (value.Contains("qualification") || value.Contains("certif")) return "qualification";
        if (value.Contains("publication")) return "publication";
        return "experience";
    }

    private static string? PreviousContext(IReadOnlyList<ProsePassage> passages, ProsePassage passage)
    {
        var index = IndexOf(passages, passage);
        if (index <= 0) return null;
        var text = passages[index - 1].Text;
        return text.Length <= 32 ? text : text[^32..];
    }

    private static string? NextContext(IReadOnlyList<ProsePassage> passages, ProsePassage passage)
    {
        var index = IndexOf(passages, passage);
        if (index < 0 || index >= passages.Count - 1) return null;
        var text = passages[index + 1].Text;
        return text.Length <= 32 ? text : text[..32];
    }

    private static int IndexOf(IReadOnlyList<ProsePassage> passages, ProsePassage passage)
    {
        for (var i = 0; i < passages.Count; i++)
            if (ReferenceEquals(passages[i], passage) || passages[i] == passage) return i;
        return -1;
    }

    internal static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "item" : slug;
    }
}
