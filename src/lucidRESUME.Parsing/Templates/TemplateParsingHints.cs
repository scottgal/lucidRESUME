using System.Text.Json.Serialization;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// Template-specific extraction rules learned from sample documents.
///
/// When a document matches a known template these hints replace generic
/// heuristics, making field extraction deterministic rather than guessed.
///
/// Populated automatically by the <c>tune</c> CLI command, or discovered
/// lazily after a high-confidence direct parse.
/// </summary>
public sealed class TemplateParsingHints
{
    // ── Style role mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Style IDs (from styles.xml) that carry the candidate's name.
    /// Usually a Heading1/Title paragraph at the very top.
    /// </summary>
    [JsonPropertyName("nameStyleIds")]
    public List<string> NameStyleIds { get; set; } = [];

    /// <summary>
    /// Style IDs that mark top-level section headers (Experience, Education…).
    /// </summary>
    [JsonPropertyName("sectionStyleIds")]
    public List<string> SectionStyleIds { get; set; } = [];

    /// <summary>
    /// Style IDs for sub-section entries, e.g. individual job titles within Experience.
    /// </summary>
    [JsonPropertyName("subSectionStyleIds")]
    public List<string> SubSectionStyleIds { get; set; } = [];

    /// <summary>
    /// Style IDs for the regular body / detail text.
    /// </summary>
    [JsonPropertyName("bodyStyleIds")]
    public List<string> BodyStyleIds { get; set; } = [];

    // ── Semantic section map ──────────────────────────────────────────────────

    /// <summary>
    /// Maps normalised heading text → semantic section type name.
    /// Keys are lower-cased, trimmed heading text (or a prefix thereof).
    /// Values are canonical names: "Experience", "Education", "Skills",
    /// "Summary", "Contact", "Projects", "Certifications", "Languages", etc.
    ///
    /// Built from observed heading paragraphs across all training samples
    /// for this template.
    /// </summary>
    [JsonPropertyName("sectionMap")]
    public Dictionary<string, string> SectionMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Layout flags ─────────────────────────────────────────────────────────

    /// <summary>Whether the template uses a Word table for its main layout.</summary>
    [JsonPropertyName("usesTableLayout")]
    public bool UsesTableLayout { get; set; }

    /// <summary>Whether the template uses a two-column text body.</summary>
    [JsonPropertyName("usesTwoColumns")]
    public bool UsesTwoColumns { get; set; }

    /// <summary>
    /// Zero-based index of the first paragraph that is NOT the name/contact header.
    /// When set, the parser can skip the header block and go straight to sections.
    /// </summary>
    [JsonPropertyName("firstSectionParagraphIndex")]
    public int? FirstSectionParagraphIndex { get; set; }

    // ── Quality ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Number of sample documents used to build/refine these hints.
    /// Higher = more reliable.
    /// </summary>
    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }

    /// <summary>Timestamp of the last hint refinement.</summary>
    [JsonPropertyName("tunedAt")]
    public DateTimeOffset? TunedAt { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when enough hints exist to drive deterministic extraction.</summary>
    [JsonIgnore]
    public bool IsUsable =>
        (NameStyleIds.Count > 0 || SectionStyleIds.Count > 0) && SectionMap.Count > 0;

    /// <summary>
    /// Returns the semantic section type for a heading text, or null if unknown.
    /// Tries exact match first, then prefix match for long/varied headings.
    /// </summary>
    public string? MapSection(string headingText)
    {
        var key = headingText.Trim().ToLowerInvariant();
        if (SectionMap.TryGetValue(key, out var exact)) return exact;

        // Prefix match — handles "Professional Experience 2020–Present" etc.
        foreach (var (k, v) in SectionMap)
        {
            if (key.StartsWith(k) || k.StartsWith(key))
                return v;
        }

        return null;
    }
}
