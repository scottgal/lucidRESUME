using System.Text.Json.Serialization;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// A named, learned template entry stored in the registry.
/// Created the first time a document is successfully parsed with high confidence,
/// or manually registered for well-known formats (LinkedIn PDF, Word default, etc.).
/// </summary>
public sealed class KnownTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; init; } = "Unknown";

    [JsonPropertyName("fingerprint")]
    public TemplateFingerprint Fingerprint { get; init; } = null!;

    /// <summary>Confidence boost applied when this template is matched (0..1).</summary>
    [JsonPropertyName("confidenceBoost")]
    public double ConfidenceBoost { get; init; } = 0.15;

    [JsonPropertyName("learnedAt")]
    public DateTimeOffset LearnedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("matchCount")]
    public int MatchCount { get; set; }
}
