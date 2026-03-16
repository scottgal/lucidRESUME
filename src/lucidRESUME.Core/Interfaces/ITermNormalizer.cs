namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Given a target vocabulary (e.g. from a job description) and a source vocabulary
/// (e.g. from the candidate's resume), finds the best matching source term for each
/// target term using semantic similarity.
///
/// Used to:
/// 1. Avoid false "missing skill" gaps when resume uses different phrasing.
/// 2. Rewrite the generated resume using the JD's exact terminology.
/// </summary>
public interface ITermNormalizer
{
    /// <summary>
    /// For each term in <paramref name="targetTerms"/>, returns the semantically
    /// nearest term from <paramref name="sourceTerms"/> and the similarity score.
    /// Returns null match when no source term meets the minimum similarity threshold.
    /// </summary>
    Task<IReadOnlyList<TermMatch>> FindMatchesAsync(
        IReadOnlyList<string> targetTerms,
        IReadOnlyList<string> sourceTerms,
        float minSimilarity = 0.82f,
        CancellationToken ct = default);
}

public record TermMatch(
    string TargetTerm,
    string? MatchedSourceTerm,   // null if no match above threshold
    float Similarity
);
