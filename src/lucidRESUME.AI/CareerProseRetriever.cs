using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public sealed record ProseRequirement(string Id, string Text, float[] Embedding);
public sealed record ProseRetrievalSignal(string RequirementId, double LexicalOverlap, double NativeScore, int? NativeRank);
/// <summary>Requirement IDs are retrieval hints, never evidence of qualification or coverage.</summary>
public sealed record RetrievedProse(CareerProseSegment Segment, double Score, IReadOnlyList<string> RequirementIds)
{
    public IReadOnlyList<ProseRetrievalSignal> Signals { get; init; } = [];
}

/// <summary>Apply career scope, status and diversity to native BM25/hybrid results; lexical fallback keeps generation available.</summary>
public static class CareerProseRetriever
{
    public static IReadOnlyList<RetrievedProse> Select(CareerProseIndex index, IEnumerable<ProseRequirement> requirements,
        string subjectId, int maximum, Func<CareerProseSegment, double>? fallbackRelevance = null,
        IReadOnlyDictionary<string, IReadOnlyList<ProseSearchHit>>? fullText = null)
    {
        if (maximum <= 0) return [];
        var segments = index.Segments.Where(segment => segment.SubjectId == subjectId && segment.Kind == "achievement").ToList();
        var terms = segments.ToDictionary(segment => segment, segment =>
            (segment.TermFrequency.Count > 0 ? segment.TermFrequency.Keys.AsEnumerable() : CareerProseLexicalIndex.Terms(segment.Text)).ToHashSet());
        var nonProduction = segments.ToDictionary(segment => segment, segment => CareerProseLexicalIndex.HasExplicitNonProductionDeployment(segment.Text));
        var fused = segments.ToDictionary(segment => segment, _ => new List<(string Id, double Score, bool Represented, ProseRetrievalSignal Signal)>());
        foreach (var query in requirements)
        {
            var queryTerms = CareerProseLexicalIndex.Terms(query.Text).ToHashSet();
            var hits = (fullText?.GetValueOrDefault(query.Id) ?? []).Where(hit => segments.Any(segment => segment.Id == hit.SegmentId))
                .DistinctBy(hit => hit.SegmentId).ToList();
            var ranks = hits.Select((hit, rank) => (hit.SegmentId, Rank: rank + 1)).ToDictionary(hit => hit.SegmentId, hit => hit.Rank);
            var scores = hits.ToDictionary(hit => hit.SegmentId, hit => hit.Score);
            var best = hits.Select(hit => hit.Score).DefaultIfEmpty(0).Max();
            var requiresProduction = queryTerms.Contains("production") && !queryTerms.Contains("prototype") && !queryTerms.Contains("pilot") &&
                !CareerProseLexicalIndex.HasExplicitNonProductionDeployment(query.Text);
            foreach (var segment in segments)
            {
                var overlap = queryTerms.Intersect(terms[segment]).Count();
                var lexical = overlap / (double)Math.Max(1, queryTerms.Count);
                var native = scores.GetValueOrDefault(segment.Id);
                var score = best > 0 ? native / best : lexical;
                score *= .65 + .35 * Math.Min(1, queryTerms.Count / 4d);
                if (requiresProduction && nonProduction[segment]) score *= .2;
                if (score <= 0) continue;
                // A fused score is a retrieval hint, never proof of qualification.
                var hasMatch = overlap >= Math.Min(2, Math.Max(1, queryTerms.Count));
                fused[segment].Add((query.Id, score, hasMatch, new(query.Id, lexical, native, ranks.TryGetValue(segment.Id, out var rank) ? rank : null)));
            }
        }
        var candidates = segments.Select(segment => new RetrievedProse(segment,
            fused[segment].Select(hit => hit.Score).DefaultIfEmpty(0).Max(), fused[segment].Where(hit => hit.Represented).Select(hit => hit.Id).ToList())
        { Signals = fused[segment].Select(hit => hit.Signal).ToList() }).ToList();
        if (candidates.All(candidate => candidate.Score <= 0))
            return candidates.Select(candidate => candidate with { Score = Math.Max(0, fallbackRelevance?.Invoke(candidate.Segment) ?? 0) })
                .OrderByDescending(candidate => candidate.Score).Take(maximum).ToList();
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

    private static double Redundancy(CareerProseSegment left, CareerProseSegment right)
    {
        var a = (left.TermFrequency.Count > 0 ? left.TermFrequency.Keys.AsEnumerable() : CareerProseLexicalIndex.Terms(left.Text)).ToHashSet();
        var b = (right.TermFrequency.Count > 0 ? right.TermFrequency.Keys.AsEnumerable() : CareerProseLexicalIndex.Terms(right.Text)).ToHashSet();
        return a.Intersect(b).Count() / (double)Math.Max(1, a.Union(b).Count());
    }
}
