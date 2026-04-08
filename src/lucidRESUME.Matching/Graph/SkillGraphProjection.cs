using UMAP;

namespace lucidRESUME.Matching.Graph;

/// <summary>
/// Projects a skill graph to 2D using UMAP on skill embeddings,
/// and characterises each Leiden community by its discriminative features.
/// </summary>
public sealed class SkillGraphProjection
{
    /// <summary>
    /// Project all embedded nodes to 2D positions via UMAP.
    /// Returns a dictionary: skill name → (x, y).
    /// </summary>
    public static Dictionary<string, (float X, float Y)> ProjectTo2D(SkillGraph graph)
    {
        var nodes = graph.Nodes.Values.Where(n => n.Embedding.Length > 0).ToList();
        if (nodes.Count < 3) return nodes.ToDictionary(n => n.SkillName, _ => (0f, 0f));

        var embeddings = nodes.Select(n => n.Embedding).ToArray();

        var umap = new Umap(
            distance: Umap.DistanceFunctions.CosineForNormalizedVectors,
            dimensions: 2,
            numberOfNeighbors: Math.Min(15, nodes.Count - 1));

        var epochs = umap.InitializeFit(embeddings);
        for (var i = 0; i < epochs; i++)
            umap.Step();

        var reduced = umap.GetEmbedding();
        var result = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
            result[nodes[i].SkillName] = (reduced[i][0], reduced[i][1]);

        return result;
    }

    /// <summary>
    /// Characterise each Leiden community by its discriminative skills.
    /// Uses a TF-IDF-like score: how much more frequent is this skill in the
    /// community vs the overall graph? The top discriminative skills define the cluster.
    /// </summary>
    public static Dictionary<int, CommunityCharacterisation> CharacteriseCommunities(SkillGraph graph)
    {
        var result = new Dictionary<int, CommunityCharacterisation>();
        var allNodes = graph.Nodes.Values.ToList();
        if (allNodes.Count == 0 || graph.CommunityCount == 0) return result;

        var totalEdgeWeight = allNodes.Sum(n => n.Edges.Values.Sum());
        if (totalEdgeWeight == 0) totalEdgeWeight = 1;

        for (var c = 0; c < graph.CommunityCount; c++)
        {
            var communityNodes = allNodes.Where(n => n.CommunityId == c).ToList();
            if (communityNodes.Count == 0) continue;

            // For each skill in this community, compute a discriminative score:
            // How central is this skill to THIS community vs the overall graph?
            var scores = new List<(string Skill, double Score, string? Category)>();

            foreach (var node in communityNodes)
            {
                // Internal edge weight: edges to other nodes in same community
                var internalWeight = node.Edges
                    .Where(kv => graph.Nodes.TryGetValue(kv.Key, out var n) && n.CommunityId == c)
                    .Sum(kv => kv.Value);

                // Total edge weight for this node
                var totalNodeWeight = node.Edges.Values.Sum();
                if (totalNodeWeight == 0) totalNodeWeight = 1;

                // Discriminative score: ratio of internal to total weight
                // Skills that ONLY connect within this community score highest
                var internalRatio = internalWeight / totalNodeWeight;

                // Boost by document frequency (skills mentioned more are more representative)
                var dfBoost = Math.Min(node.DocumentFrequency / 3.0, 1.0);

                scores.Add((node.SkillName, internalRatio * (0.5 + dfBoost * 0.5), node.Category));
            }

            var ranked = scores.OrderByDescending(s => s.Score).ToList();

            // Auto-generate a label from top discriminative skills
            var topSkills = ranked.Take(3).Select(s => s.Skill).ToList();
            var dominantCategory = ranked
                .Where(s => s.Category != null)
                .GroupBy(s => s.Category!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            var label = dominantCategory != null
                ? $"{dominantCategory} ({string.Join(", ", topSkills)})"
                : string.Join(", ", topSkills);

            result[c] = new CommunityCharacterisation
            {
                CommunityId = c,
                Label = label,
                DominantCategory = dominantCategory,
                DiscriminativeSkills = ranked.Select(s => new DiscriminativeSkill
                {
                    SkillName = s.Skill,
                    Score = s.Score,
                    Category = s.Category,
                }).ToList(),
                MemberCount = communityNodes.Count,
            };
        }

        return result;
    }
}

public sealed class CommunityCharacterisation
{
    public int CommunityId { get; init; }
    public string Label { get; init; } = "";
    public string? DominantCategory { get; init; }
    public List<DiscriminativeSkill> DiscriminativeSkills { get; init; } = [];
    public int MemberCount { get; init; }
}

public sealed class DiscriminativeSkill
{
    public string SkillName { get; init; } = "";
    public double Score { get; init; }
    public string? Category { get; init; }
}
