namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Lightweight LLM-based extraction fallback.  Used after rule-based parsing to
/// recover fields that heuristics missed (skills, experience, education).
/// Designed for a small, fast local model (e.g. qwen3.5:0.6b via Ollama).
/// </summary>
public interface ILlmExtractionService
{
    bool IsAvailable { get; }

    /// <summary>Returns comma-separated skill names extracted from text, or null on failure.</summary>
    Task<string?> ExtractSkillsAsync(string text, CancellationToken ct = default);

    /// <summary>Returns a plain-text summary of work experience, or null on failure.</summary>
    Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Extracts the candidate's full name from a small text chunk (first few lines of a resume).
    /// Returns just the name, or null on failure.
    /// </summary>
    Task<string?> ExtractNameAsync(string headerText, CancellationToken ct = default);

    /// <summary>
    /// Extracts structured JD fields as JSON. Returns raw JSON string or null on failure.
    /// Prompt is pre-built by the caller — this just sends it and returns the response.
    /// </summary>
    Task<string?> ExtractJsonAsync(string prompt, CancellationToken ct = default) =>
        ExtractSkillsAsync(prompt, ct); // Default implementation reuses skills endpoint
}
