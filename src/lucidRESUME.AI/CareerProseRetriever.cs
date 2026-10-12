using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public sealed record ProseRequirement(string Id, string Text, float[] Embedding);
public sealed record RetrievedProse(CareerProseSegment Segment, double Score, IReadOnlyList<string> RequirementIds);

/// <summary>Hybrid retrieval with coverage and diversity. Similarity ranks evidence; it never grants a skill.</summary>
public static class CareerProseRetriever
{
    private static readonly HashSet<string> StopWords = "the and for with from that this will have your our you work build built design designed development engineering technical systems platform production experience".Split(' ').ToHashSet();
    private static HashSet<string> Terms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z0-9+#.-]*")
        .Select(match => match.Value.TrimEnd('.')).Where(term => term.Length > 1 && !StopWords.Contains(term)).ToHashSet();

    public static IReadOnlyList<RetrievedProse> Select(CareerProseIndex index, IEnumerable<ProseRequirement> requirements,
        string subjectId, int maximum, Func<CareerProseSegment, double>? fallbackRelevance = null)
    {
        var queries = requirements.Select(query => (Query: query, Terms: Terms(query.Text))).ToList();
        var candidates = index.Segments.Where(segment => segment.SubjectId == subjectId && segment.Kind == "achievement")
            .Select(segment =>
            {
                var terms = Terms(segment.Text);
                var scores = queries.Select(query =>
                {
                    var lexical = query.Terms.Count == 0 ? 0 : terms.Intersect(query.Terms).Count() / Math.Sqrt(query.Terms.Count * Math.Max(1d, terms.Count));
                    var semantic = Math.Max(0, Cosine(query.Query.Embedding, segment.Embedding));
                    // Semantic neighbors may be useful to the writer, but are not a coverage finding.
                    return (query.Query.Id, Score: .7 * lexical + .3 * semantic, Supported: lexical > 0);
                }).ToList();
                return new RetrievedProse(segment, scores.Select(score => score.Score).DefaultIfEmpty(0).Max(),
                    scores.Where(score => score.Supported).Select(score => score.Id).ToList());
            }).ToList();
        if (candidates.All(candidate => candidate.Score <= 0))
            return candidates.OrderByDescending(candidate => fallbackRelevance?.Invoke(candidate.Segment) ?? 0).Take(maximum).ToList();
        // Diversity is useful among relevant passages; it must not promote an
        // unrelated example merely because its vocabulary differs.
        if (candidates.Any(candidate => candidate.Score > 0))
            candidates.RemoveAll(candidate => candidate.Score <= 0);
        var selected = new List<RetrievedProse>();
        var covered = new HashSet<string>();
        while (candidates.Count > 0 && selected.Count < maximum)
        {
            var next = candidates.OrderByDescending(candidate => candidate.Score
                    + .12 * candidate.RequirementIds.Count(id => !covered.Contains(id))
                    - .15 * selected.Select(item => Redundancy(candidate.Segment, item.Segment)).DefaultIfEmpty(0).Max())
                .ThenBy(candidate => candidate.Segment.Id, StringComparer.Ordinal).First();
            selected.Add(next);
            covered.UnionWith(next.RequirementIds);
            candidates.Remove(next);
        }
        return selected;
    }

    private static double Redundancy(CareerProseSegment left, CareerProseSegment right)
    {
        var a = Terms(left.Text);
        var b = Terms(right.Text);
        return a.Intersect(b).Count() / (double)Math.Max(1, a.Union(b).Count());
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        var score = aa > 0 && bb > 0 ? dot / Math.Sqrt(aa * bb) : 0;
        return double.IsFinite(score) ? score : 0;
    }
}
