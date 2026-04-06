using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching.Graph;

/// <summary>
/// Generates job search queries from skill graph communities.
/// Each community produces a query that targets roles in that cluster.
/// Also generates "bridge" queries that target adjacent communities.
/// </summary>
public sealed class SearchQueryGenerator
{
    /// <summary>
    /// Generate search queries from the strongest skill communities.
    /// Returns queries ranked by the user's strength in each community.
    /// </summary>
    public List<SuggestedQuery> Generate(SkillGraph graph, SkillLedger resumeLedger)
    {
        var queries = new List<SuggestedQuery>();
        var summaries = graph.GetCommunitySummaries(topN: 6);

        foreach (var (communityId, topSkills) in summaries)
        {
            if (topSkills.Count < 2) continue;

            // Calculate your strength in this community
            var communityNodes = graph.GetCommunity(communityId);
            var yourStrength = communityNodes
                .Select(n => resumeLedger.Find(n.SkillName))
                .Where(e => e is not null)
                .Average(e => e!.Strength);

            var evidencedCount = communityNodes
                .Count(n => resumeLedger.Find(n.SkillName) is { Strength: > 0.3 });

            // Generate query from top skills
            var queryTerms = topSkills.Take(3);
            var query = string.Join(" ", queryTerms);

            queries.Add(new SuggestedQuery
            {
                Query = query,
                CommunityId = communityId,
                CommunitySkills = topSkills,
                YourStrength = yourStrength,
                EvidencedSkillCount = evidencedCount,
                TotalSkillCount = communityNodes.Count,
                QueryType = yourStrength > 0.5 ? QueryType.StrongFit
                    : yourStrength > 0.2 ? QueryType.GrowthTarget
                    : QueryType.StretchGoal,
                Description = yourStrength > 0.5
                    ? $"Strong fit — you have {evidencedCount}/{communityNodes.Count} skills with solid evidence"
                    : yourStrength > 0.2
                    ? $"Growth target — you have foundations, {communityNodes.Count - evidencedCount} skills to develop"
                    : $"Stretch goal — would require significant upskilling in {string.Join(", ", topSkills.Take(2))}",
            });
        }

        return queries.OrderByDescending(q => q.YourStrength).ToList();
    }

    /// <summary>
    /// Generate "next step" queries — roles that bridge between your
    /// strong community and an adjacent target community.
    /// </summary>
    public List<SuggestedQuery> GenerateBridgeQueries(
        SkillGraph graph, SkillLedger resumeLedger, int targetCommunityId)
    {
        var queries = new List<SuggestedQuery>();
        var targetSkills = graph.GetCommunity(targetCommunityId);
        if (targetSkills.Count == 0) return queries;

        // Find skills you already have that connect to the target community
        var bridgeSkills = new List<string>();
        foreach (var entry in resumeLedger.StrongSkills)
        {
            if (!graph.Nodes.TryGetValue(entry.SkillName, out var node)) continue;

            // Check if this skill has edges to the target community
            var targetConnections = node.Edges.Keys
                .Where(k => graph.Nodes.TryGetValue(k, out var n) && n.CommunityId == targetCommunityId)
                .Count();

            if (targetConnections > 0)
                bridgeSkills.Add(entry.SkillName);
        }

        if (bridgeSkills.Count > 0)
        {
            // Query: your bridge skills + target community top skills
            var targetTop = targetSkills
                .OrderByDescending(n => n.Edges.Values.Sum())
                .Take(2)
                .Select(n => n.SkillName);

            var query = string.Join(" ", bridgeSkills.Take(2).Concat(targetTop));

            queries.Add(new SuggestedQuery
            {
                Query = query,
                CommunityId = targetCommunityId,
                CommunitySkills = targetSkills.Select(n => n.SkillName).Take(6).ToList(),
                YourStrength = 0.3, // medium — you have bridges but not the target
                EvidencedSkillCount = bridgeSkills.Count,
                TotalSkillCount = targetSkills.Count,
                QueryType = QueryType.BridgeRole,
                Description = $"Bridge role — combines your {string.Join(", ", bridgeSkills.Take(2))} " +
                    $"with {string.Join(", ", targetTop)} to move into this cluster",
            });
        }

        return queries;
    }
}

public sealed class SuggestedQuery
{
    public string Query { get; set; } = "";
    public int CommunityId { get; set; }
    public List<string> CommunitySkills { get; set; } = [];
    public double YourStrength { get; set; }
    public int EvidencedSkillCount { get; set; }
    public int TotalSkillCount { get; set; }
    public QueryType QueryType { get; set; }
    public string Description { get; set; } = "";
}

public enum QueryType
{
    StrongFit,     // you're already well-positioned for these roles
    GrowthTarget,  // achievable with some skill development
    StretchGoal,   // aspirational — significant gap to close
    BridgeRole,    // stepping stone between your current and target cluster
}
