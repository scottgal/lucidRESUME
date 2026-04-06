namespace lucidRESUME.Matching.Graph;

/// <summary>
/// A node in the skill graph — represents a unique skill with its embedding.
/// Connected to other skills by co-occurrence in jobs (resume or JD).
/// </summary>
public sealed class SkillNode
{
    public string SkillName { get; init; } = "";
    public float[] Embedding { get; set; } = [];
    public int CommunityId { get; set; } = -1;
    public string? Category { get; set; }

    /// <summary>How many resumes/JDs mention this skill.</summary>
    public int DocumentFrequency { get; set; }

    /// <summary>Edges to co-occurring skills (skill name → edge weight).</summary>
    public Dictionary<string, double> Edges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
