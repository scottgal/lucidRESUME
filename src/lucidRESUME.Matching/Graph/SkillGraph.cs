using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching.Graph;

/// <summary>
/// Skill graph built from resume and JD skill ledgers.
/// Skills are nodes, co-occurrence in the same job/role creates edges.
/// Community detection clusters skills into natural groups.
/// </summary>
public sealed class SkillGraph
{
    public Dictionary<string, SkillNode> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int CommunityCount { get; private set; }

    /// <summary>Add skills from a resume ledger — edges from co-occurrence in same role.</summary>
    public void AddResumeLedger(SkillLedger ledger)
    {
        // Group skills by experience entry they appear in
        var skillsByRole = new Dictionary<Guid, List<string>>();
        foreach (var entry in ledger.Entries)
        {
            EnsureNode(entry.SkillName, entry.Category);
            foreach (var evidence in entry.Evidence.Where(e => e.ExperienceId.HasValue))
            {
                var roleId = evidence.ExperienceId!.Value;
                if (!skillsByRole.TryGetValue(roleId, out var list))
                    skillsByRole[roleId] = list = [];
                if (!list.Contains(entry.SkillName, StringComparer.OrdinalIgnoreCase))
                    list.Add(entry.SkillName);
            }
        }

        // Create edges between all skills that co-occur in the same role
        foreach (var (_, skills) in skillsByRole)
        {
            for (int i = 0; i < skills.Count; i++)
            for (int j = i + 1; j < skills.Count; j++)
                AddEdge(skills[i], skills[j], 1.0);
        }
    }

    /// <summary>Add skills from a JD ledger — all required skills co-occur.</summary>
    public void AddJdLedger(JdSkillLedger jdLedger)
    {
        var skills = jdLedger.Requirements.Select(r => r.SkillName).ToList();
        foreach (var skill in skills)
            EnsureNode(skill);

        // All skills in a JD co-occur
        for (int i = 0; i < skills.Count; i++)
        for (int j = i + 1; j < skills.Count; j++)
        {
            var weight = jdLedger.Requirements[i].Importance == SkillImportance.Required &&
                         jdLedger.Requirements[j].Importance == SkillImportance.Required
                ? 2.0 : 1.0;
            AddEdge(skills[i], skills[j], weight);
        }
    }

    /// <summary>Embed all nodes using the embedding service.</summary>
    public async Task EmbedAsync(IEmbeddingService embedder, CancellationToken ct = default)
    {
        foreach (var node in Nodes.Values)
        {
            if (node.Embedding.Length == 0)
                node.Embedding = await embedder.EmbedAsync(node.SkillName, ct);
        }
    }

    /// <summary>
    /// Simple Louvain-style community detection (greedy modularity optimization).
    /// Not full Leiden but captures the main communities effectively for small graphs.
    /// </summary>
    public void DetectCommunities()
    {
        var nodes = Nodes.Values.ToList();
        if (nodes.Count == 0) return;

        // Initialize: each node in its own community
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].CommunityId = i;

        var totalWeight = nodes.Sum(n => n.Edges.Values.Sum()) / 2.0;
        if (totalWeight == 0) { CommunityCount = nodes.Count; return; }

        bool changed = true;
        int iterations = 0;

        while (changed && iterations++ < 50)
        {
            changed = false;
            foreach (var node in nodes)
            {
                var bestCommunity = node.CommunityId;
                var bestGain = 0.0;

                // Try moving this node to each neighbor's community
                var neighborCommunities = node.Edges.Keys
                    .Where(k => Nodes.ContainsKey(k))
                    .Select(k => Nodes[k].CommunityId)
                    .Distinct();

                foreach (var candidateCommunity in neighborCommunities)
                {
                    if (candidateCommunity == node.CommunityId) continue;
                    var gain = ModularityGain(node, candidateCommunity, nodes, totalWeight);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCommunity = candidateCommunity;
                    }
                }

                if (bestCommunity != node.CommunityId)
                {
                    node.CommunityId = bestCommunity;
                    changed = true;
                }
            }
        }

        // Renumber communities to be contiguous 0..N
        var communityMap = nodes.Select(n => n.CommunityId).Distinct().Order()
            .Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        foreach (var node in nodes)
            node.CommunityId = communityMap[node.CommunityId];
        CommunityCount = communityMap.Count;
    }

    /// <summary>Get all skills in a community.</summary>
    public List<SkillNode> GetCommunity(int communityId) =>
        Nodes.Values.Where(n => n.CommunityId == communityId).ToList();

    /// <summary>Get community centroids as lists of top skills.</summary>
    public Dictionary<int, List<string>> GetCommunitySummaries(int topN = 5) =>
        Enumerable.Range(0, CommunityCount)
            .ToDictionary(
                c => c,
                c => GetCommunity(c)
                    .OrderByDescending(n => n.Edges.Values.Sum())
                    .Take(topN)
                    .Select(n => n.SkillName)
                    .ToList());

    private void EnsureNode(string skillName, string? category = null)
    {
        if (!Nodes.ContainsKey(skillName))
            Nodes[skillName] = new SkillNode { SkillName = skillName, Category = category };
        Nodes[skillName].DocumentFrequency++;
    }

    private void AddEdge(string a, string b, double weight)
    {
        if (!Nodes.ContainsKey(a) || !Nodes.ContainsKey(b)) return;
        Nodes[a].Edges.TryGetValue(b, out var w1);
        Nodes[a].Edges[b] = w1 + weight;
        Nodes[b].Edges.TryGetValue(a, out var w2);
        Nodes[b].Edges[a] = w2 + weight;
    }

    private static double ModularityGain(SkillNode node, int targetCommunity,
        List<SkillNode> allNodes, double totalWeight)
    {
        // Sum of edges from node to nodes in target community
        var ki_in = node.Edges
            .Where(kv => Nodes_GetCommunity(allNodes, kv.Key) == targetCommunity)
            .Sum(kv => kv.Value);
        var ki = node.Edges.Values.Sum();
        var sigma_tot = allNodes
            .Where(n => n.CommunityId == targetCommunity)
            .Sum(n => n.Edges.Values.Sum());

        return ki_in / totalWeight - (sigma_tot * ki) / (2 * totalWeight * totalWeight);
    }

    private static int Nodes_GetCommunity(List<SkillNode> nodes, string name) =>
        nodes.FirstOrDefault(n => n.SkillName.Equals(name, StringComparison.OrdinalIgnoreCase))?.CommunityId ?? -1;
}
