namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Tuneable parameters for JD field extraction and RRF fusion.
/// Loaded from appsettings.json section "JdFusion".
/// </summary>
public sealed class FusionOptions
{
    /// <summary>RRF constant (higher = more weight to top ranks).</summary>
    public double RrfK { get; set; } = 60.0;

    /// <summary>Extra confidence boost per additional source that agrees.</summary>
    public double MultiSourceBoost { get; set; } = 0.1;

    /// <summary>Minimum confidence for a candidate to be included in results.</summary>
    public double MinConfidence { get; set; } = 0.3;

    /// <summary>Minimum NER entity confidence to contribute.</summary>
    public double NerMinConfidence { get; set; } = 0.5;

    /// <summary>Minimum entity text length from NER.</summary>
    public int NerMinLength { get; set; } = 2;

    /// <summary>Base confidence for structural extractor candidates.</summary>
    public double StructuralBaseConfidence { get; set; } = 0.8;

    /// <summary>Base confidence for LLM extractor candidates.</summary>
    public double LlmBaseConfidence { get; set; } = 0.85;
}
