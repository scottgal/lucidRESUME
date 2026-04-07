namespace lucidRESUME.AI;

/// <summary>
/// Tunable parameters for resume tailoring - prompts, weights, and thresholds.
/// All values are JSON-configurable via the "Tailoring" appsettings section.
/// </summary>
public sealed class TailoringOptions
{
    /// <summary>Minimum cosine similarity for term-normalisation matches.</summary>
    public float TermNormalizationMinSimilarity { get; set; } = 0.85f;

    /// <summary>
    /// Tone guidance injected into the tailoring prompt per company type.
    /// Keys must match <see cref="lucidRESUME.Core.Models.Jobs.CompanyType"/> enum names.
    /// </summary>
    public Dictionary<string, string> CompanyTones { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Startup"]     = "Tone: emphasise ownership, breadth, speed of delivery, and shipped outcomes. " +
                          "De-emphasise process-heavy corporate language.",
        ["ScaleUp"]     = "Tone: emphasise building systems at scale, repeatability, and team/function growth. " +
                          "Show you can take things from scrappy to structured.",
        ["Enterprise"]  = "Tone: emphasise process adherence, risk management, stakeholder communication, " +
                          "and delivery within constraints. Use precise, professional language.",
        ["Agency"]      = "Tone: emphasise speed, client communication, multi-project delivery, " +
                          "and breadth of domain exposure.",
        ["Consultancy"] = "Tone: emphasise structured problem-solving, stakeholder management, " +
                          "frameworks, and on-time delivery across engagements.",
        ["Finance"]     = "Tone: emphasise accuracy, compliance awareness, quantified impact, " +
                          "and regulated-environment experience. Every bullet should have a number.",
        ["Public"]      = "Tone: emphasise service delivery, accessibility, stakeholder diversity, " +
                          "and policy/compliance alignment.",
        ["Academic"]    = "Tone: emphasise research rigour, publications, teaching, and methodological depth.",
    };
}