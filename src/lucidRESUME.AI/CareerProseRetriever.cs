using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public sealed record ProseRequirement(string Id, string Text, float[] Embedding);
public sealed record ProseRetrievalSignal(string RequirementId, double TfIdf, double Semantic, int? FullTextRank);
/// <summary>Requirement IDs are retrieval hints, never evidence of qualification or coverage.</summary>
public sealed record RetrievedProse(CareerProseSegment Segment, double Score, IReadOnlyList<string> RequirementIds)
{
    public IReadOnlyList<ProseRetrievalSignal> Signals { get; init; } = [];
}

/// <summary>Rank fusion of persisted sparse vectors, dense similarity and SQLite FTS5/BM25.</summary>
public static class CareerProseRetriever
{
    public static IReadOnlyList<RetrievedProse> Select(CareerProseIndex index, IEnumerable<ProseRequirement> requirements,
        string subjectId, int maximum, Func<CareerProseSegment, double>? fallbackRelevance = null,
        IReadOnlyDictionary<string, IReadOnlyList<ProseSearchHit>>? fullText = null)
    {
        if (maximum <= 0) return [];
        var segments = index.Segments.Where(segment => segment.SubjectId == subjectId && segment.Kind == "achievement").ToList();
        var frequencies = index.DocumentFrequency.Count > 0 ? index.DocumentFrequency : index.Segments
            .SelectMany(segment => CareerProseLexicalIndex.Terms(segment.Text).Distinct()).GroupBy(term => term).ToDictionary(group => group.Key, group => group.Count());
        Dictionary<string, double> Weights(string text) => Normalize(CareerProseLexicalIndex.Terms(text).GroupBy(term => term)
            .ToDictionary(group => group.Key, group => (1 + Math.Log(group.Count())) *
                (1 + Math.Log((1d + index.Segments.Count) / (1 + frequencies.GetValueOrDefault(group.Key))))));
        var weights = segments.ToDictionary(segment => segment, segment => segment.TfIdf.Count > 0 ? segment.TfIdf : Weights(segment.Text));
        var fused = segments.ToDictionary(segment => segment, _ => new List<(string Id, double Score, bool Represented, ProseRetrievalSignal Signal)>());
        foreach (var query in requirements)
        {
            var queryWeights = Weights(query.Text);
            var sparse = segments.ToDictionary(segment => segment, segment => queryWeights.Sum(term => term.Value * weights[segment].GetValueOrDefault(term.Key)));
            var dense = segments.ToDictionary(segment => segment, segment => segment.EmbeddingSpace == index.EmbeddingSpace
                ? Math.Max(0, Cosine(query.Embedding, segment.Embedding)) : 0);
            var ranks = (fullText?.GetValueOrDefault(query.Id) ?? []).Where(hit => segments.Any(segment => segment.Id == hit.SegmentId))
                .Select((hit, rank) => (hit.SegmentId, Rank: rank + 1)).DistinctBy(hit => hit.SegmentId).ToDictionary(hit => hit.SegmentId, hit => hit.Rank);
            var sparseRanks = Rank(sparse);
            var denseRanks = Rank(dense);
            var channels = (sparseRanks.Count > 0 ? 1 : 0) + (denseRanks.Count > 0 ? 1 : 0) + (ranks.Count > 0 ? 1 : 0);
            foreach (var segment in segments)
            {
                var score = 60d * (Reciprocal(sparseRanks.GetValueOrDefault(segment)) + Reciprocal(denseRanks.GetValueOrDefault(segment)) +
                    Reciprocal(ranks.GetValueOrDefault(segment.Id))) / Math.Max(1, channels);
                // Rank alone loses match strength: a single generic word can rank first
                // for a sparse query. Keep concrete multi-term accounts ahead of that case.
                score *= (.65 + .35 * Math.Min(1, queryWeights.Count / 4d)) * (.5 + .5 * Math.Max(sparse[segment], dense[segment]));
                if (queryWeights.ContainsKey("production") && !queryWeights.ContainsKey("prototype") && !queryWeights.ContainsKey("pilot") &&
                    !CareerProseLexicalIndex.HasExplicitNonProductionDeployment(query.Text) &&
                    CareerProseLexicalIndex.HasExplicitNonProductionDeployment(segment.Text)) score *= .2;
                if (score <= 0) continue;
                var overlap = queryWeights.Keys.Intersect(weights[segment].Keys).Count();
                var hasMatch = overlap >= Math.Min(2, Math.Max(1, queryWeights.Count)) || dense[segment] >= .55;
                fused[segment].Add((query.Id, score, hasMatch, new(query.Id, sparse[segment], dense[segment], ranks.TryGetValue(segment.Id, out var rank) ? rank : null)));
            }
        }
        var candidates = segments.Select(segment => new RetrievedProse(segment,
            fused[segment].Select(hit => hit.Score).DefaultIfEmpty(0).Max(), fused[segment].Where(hit => hit.Represented).Select(hit => hit.Id).ToList())
        { Signals = fused[segment].Select(hit => hit.Signal).ToList() }).ToList();
        if (candidates.All(candidate => candidate.Score <= 0))
            return candidates.OrderByDescending(candidate => fallbackRelevance?.Invoke(candidate.Segment) ?? 0).Take(maximum).ToList();
        candidates.RemoveAll(candidate => candidate.Score <= 0);
        var selected = new List<RetrievedProse>();
        var represented = new HashSet<string>();
        while (candidates.Count > 0 && selected.Count < maximum)
        {
            var next = candidates.OrderByDescending(candidate => candidate.Score
                    + .12 * candidate.RequirementIds.Count(id => !represented.Contains(id))
                    - .15 * selected.Select(item => Redundancy(candidate.Segment, item.Segment)).DefaultIfEmpty(0).Max())
                .ThenByDescending(candidate => candidate.Segment.Salience)
                .ThenBy(candidate => candidate.Segment.Id, StringComparer.Ordinal).First();
            selected.Add(next);
            represented.UnionWith(next.RequirementIds);
            candidates.Remove(next);
        }
        return selected;
    }

    private static Dictionary<CareerProseSegment, int> Rank(Dictionary<CareerProseSegment, double> scores) => scores.Where(item => item.Value > 0)
        .OrderByDescending(item => item.Value).ThenBy(item => item.Key.Id, StringComparer.Ordinal).Select((item, rank) => (item.Key, Rank: rank + 1))
        .ToDictionary(item => item.Key, item => item.Rank);
    private static double Reciprocal(int rank) => rank > 0 ? 1d / (60 + rank) : 0;
    private static Dictionary<string, double> Normalize(Dictionary<string, double> weights)
    {
        var norm = Math.Sqrt(weights.Values.Sum(weight => weight * weight));
        return weights.ToDictionary(term => term.Key, term => norm > 0 ? term.Value / norm : 0);
    }
    private static double Redundancy(CareerProseSegment left, CareerProseSegment right)
    {
        var a = (left.TermFrequency.Count > 0 ? left.TermFrequency.Keys.AsEnumerable() : CareerProseLexicalIndex.Terms(left.Text)).ToHashSet();
        var b = (right.TermFrequency.Count > 0 ? right.TermFrequency.Keys.AsEnumerable() : CareerProseLexicalIndex.Terms(right.Text)).ToHashSet();
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
