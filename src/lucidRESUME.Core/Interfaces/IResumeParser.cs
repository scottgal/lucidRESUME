using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface IResumeParser
{
    Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default);
    Task<ResumeDocument> ParseAsync(string filePath, ParseMode mode, CancellationToken ct = default) =>
        ParseAsync(filePath, ct); // default implementation ignores mode for backward compat
}

/// <summary>
/// Controls the extraction pipeline depth.
/// Fast: structural parsing + NER only (sub-second, no external services).
/// AI: adds LLM fallback for missing fields (requires Ollama or cloud API).
/// Full: adds Docling for ML-based PDF layout detection (requires Docker).
/// </summary>
public enum ParseMode
{
    /// <summary>Structural + NER only. Fastest. No external dependencies.</summary>
    Fast,
    /// <summary>Structural + NER + LLM fallback. Requires Ollama or API key.</summary>
    AI,
    /// <summary>Structural + NER + LLM + Docling. Requires Docker + Ollama.</summary>
    Full
}
