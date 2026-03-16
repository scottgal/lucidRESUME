using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Matching;

/// <summary>
/// Semantic term normalizer backed by <see cref="IEmbeddingService"/>.
/// Embeds all terms once, then scores all target×source pairs.
/// Typical usage: ~20 JD skills × ~30 resume skills = 600 pairs, negligible cost after embedding.
/// </summary>
public sealed class SemanticTermNormalizer : ITermNormalizer
{
    private readonly IEmbeddingService _embedder;

    public SemanticTermNormalizer(IEmbeddingService embedder) => _embedder = embedder;

    public async Task<IReadOnlyList<TermMatch>> FindMatchesAsync(
        IReadOnlyList<string> targetTerms,
        IReadOnlyList<string> sourceTerms,
        float minSimilarity = 0.82f,
        CancellationToken ct = default)
    {
        if (targetTerms.Count == 0 || sourceTerms.Count == 0)
            return targetTerms.Select(t => new TermMatch(t, null, 0f)).ToList();

        // Embed all terms in parallel (cache makes repeated calls free)
        var targetEmbeddings = await Task.WhenAll(
            targetTerms.Select(t => _embedder.EmbedAsync(t, ct)));
        var sourceEmbeddings = await Task.WhenAll(
            sourceTerms.Select(s => _embedder.EmbedAsync(s, ct)));

        var results = new List<TermMatch>(targetTerms.Count);

        for (int i = 0; i < targetTerms.Count; i++)
        {
            float bestSim  = -1f;
            int   bestIdx  = -1;

            for (int j = 0; j < sourceTerms.Count; j++)
            {
                float sim = _embedder.CosineSimilarity(targetEmbeddings[i], sourceEmbeddings[j]);
                if (sim > bestSim)
                {
                    bestSim = sim;
                    bestIdx = j;
                }
            }

            results.Add(bestSim >= minSimilarity && bestIdx >= 0
                ? new TermMatch(targetTerms[i], sourceTerms[bestIdx], bestSim)
                : new TermMatch(targetTerms[i], null, bestSim));
        }

        return results;
    }
}
