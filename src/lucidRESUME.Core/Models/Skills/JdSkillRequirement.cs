namespace lucidRESUME.Core.Models.Skills;

/// <summary>
/// A skill requirement from a job description, with importance weighting.
/// </summary>
public sealed class JdSkillRequirement
{
    public string SkillName { get; set; } = "";
    public SkillImportance Importance { get; set; }

    /// <summary>The source text that mentions this requirement.</summary>
    public string? SourceText { get; set; }

    /// <summary>Embedding vector for this skill (cached from IEmbeddingService).</summary>
    public float[]? Embedding { get; set; }
}

public enum SkillImportance
{
    Required,       // explicitly required
    Preferred,      // nice-to-have
    Inferred,       // mentioned in description but not in requirements list
}
