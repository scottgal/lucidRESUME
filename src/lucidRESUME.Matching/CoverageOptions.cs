namespace lucidRESUME.Matching;

public sealed class CoverageOptions
{
    /// <summary>Cosine-similarity threshold for skill/tech semantic matching.</summary>
    public float SkillSemanticThreshold { get; set; } = 0.82f;

    /// <summary>Cosine-similarity threshold for responsibility semantic matching.</summary>
    public float ResponsibilitySemanticThreshold { get; set; } = 0.75f;

    /// <summary>Minimum keyword overlap count to count a responsibility as covered (keyword matching).</summary>
    public int MinKeywordOverlap { get; set; } = 2;

    /// <summary>Minimum keyword length to be considered a signal word.</summary>
    public int MinKeywordLength { get; set; } = 4;

    /// <summary>Words ignored when extracting signal keywords from responsibility text.</summary>
    public List<string> StopWords { get; set; } =
    [
        "about", "above", "after", "also", "among", "being", "between",
        "their", "there", "these", "those", "through", "using", "where",
        "which", "while", "will", "with", "working", "within", "would",
        "experience", "ability", "knowledge", "skills", "strong", "across"
    ];
}
