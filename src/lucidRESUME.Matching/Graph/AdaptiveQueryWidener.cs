using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching.Graph;

/// <summary>
/// Adaptive query widening. When a search returns too few results,
/// automatically broadens the query by:
/// 1. Dropping the most specific terms
/// 2. Substituting synonyms from the skill graph (adjacent nodes)
/// 3. Trying queries from adjacent communities
///
/// Returns a ranked list of progressively wider queries.
/// </summary>
public sealed class AdaptiveQueryWidener
{
    /// <summary>
    /// Generate progressively wider queries from an initial query.
    /// Each level drops specificity while maintaining relevance.
    /// </summary>
    public List<WidenedQuery> Widen(
        string originalQuery,
        SkillGraph graph,
        SkillLedger resumeLedger,
        int maxLevels = 4)
    {
        var results = new List<WidenedQuery>();
        var queryTerms = originalQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Level 0: Original query
        results.Add(new WidenedQuery
        {
            Query = originalQuery,
            Level = 0,
            Strategy = "Original",
            Description = "Exact query from skill community"
        });

        if (queryTerms.Count < 2) return results;

        // Level 1: Drop the most specific term (shortest or least connected in graph)
        var sortedByGenerality = queryTerms
            .OrderBy(t => graph.Nodes.TryGetValue(t, out var n) ? n.Edges.Count : 0)
            .ToList();

        var broadened = sortedByGenerality.Skip(1).ToList(); // drop least connected
        if (broadened.Count >= 2)
        {
            results.Add(new WidenedQuery
            {
                Query = string.Join(" ", broadened),
                Level = 1,
                Strategy = "Drop specific",
                Description = $"Removed '{sortedByGenerality[0]}' (most specific)"
            });
        }

        // Level 2: Substitute with adjacent skills (graph neighbors)
        var substitutions = new List<string>();
        foreach (var term in queryTerms)
        {
            if (!graph.Nodes.TryGetValue(term, out var node)) continue;

            // Find the strongest neighbor NOT already in the query
            var bestNeighbor = node.Edges
                .Where(kv => !queryTerms.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (bestNeighbor != null)
                substitutions.Add(bestNeighbor);
        }

        if (substitutions.Count > 0)
        {
            var synonymQuery = string.Join(" ", queryTerms.Take(2).Concat(substitutions.Take(2)));
            results.Add(new WidenedQuery
            {
                Query = synonymQuery,
                Level = 2,
                Strategy = "Synonym substitution",
                Description = $"Added related: {string.Join(", ", substitutions.Take(2))}"
            });
        }

        // Level 3: Use role-title based query instead of skill-based
        var topRoles = resumeLedger.Entries
            .SelectMany(e => e.Evidence)
            .Where(ev => ev.JobTitle != null)
            .Select(ev => ev.JobTitle!)
            .Distinct()
            .Take(3)
            .ToList();

        if (topRoles.Count > 0)
        {
            // Use the most recent job title as the query
            results.Add(new WidenedQuery
            {
                Query = topRoles[0],
                Level = 3,
                Strategy = "Role title",
                Description = $"Search by your most recent title"
            });
        }

        // Level 4: Adjacent community query
        if (graph.CommunityCount > 1)
        {
            // Find the community you're strongest in
            var yourCommunity = graph.Nodes.Values
                .Where(n => resumeLedger.Find(n.SkillName) is { Strength: > 0.3 })
                .GroupBy(n => n.CommunityId)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (yourCommunity != null)
            {
                // Find an adjacent community (shares edges but different ID)
                var adjacentCommunities = graph.Nodes.Values
                    .Where(n => n.CommunityId != yourCommunity.Key)
                    .Where(n => n.Edges.Keys.Any(k =>
                        graph.Nodes.TryGetValue(k, out var neighbor) &&
                        neighbor.CommunityId == yourCommunity.Key))
                    .GroupBy(n => n.CommunityId)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (adjacentCommunities != null)
                {
                    var adjacentTop = adjacentCommunities
                        .OrderByDescending(n => n.Edges.Values.Sum())
                        .Take(3)
                        .Select(n => n.SkillName);

                    results.Add(new WidenedQuery
                    {
                        Query = string.Join(" ", adjacentTop),
                        Level = 4,
                        Strategy = "Adjacent community",
                        Description = "Skills from a neighboring cluster you could bridge into"
                    });
                }
            }
        }

        return results;
    }
}

public sealed class WidenedQuery
{
    public string Query { get; init; } = "";
    public int Level { get; init; }
    public string Strategy { get; init; } = "";
    public string Description { get; init; } = "";
}
