namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Generates dense vector embeddings for text.
/// Default implementation calls Ollama nomic-embed-text.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Returns a normalised embedding vector for the given text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Cosine similarity between two normalised vectors.</summary>
    float CosineSimilarity(float[] a, float[] b);
}
