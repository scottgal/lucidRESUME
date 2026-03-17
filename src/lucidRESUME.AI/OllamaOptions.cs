namespace lucidRESUME.AI;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Main tailoring model. Qwen3 family — thinking is disabled at call time.</summary>
    public string Model { get; set; } = "qwen3.5:4b";
    /// <summary>Small fast model for field extraction fallback.</summary>
    public string ExtractionModel { get; set; } = "qwen3.5:0.8b";
    /// <summary>Embedding model for semantic similarity. nomic-embed-text is the recommended local model.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    /// <summary>
    /// KV-cache context window (tokens). Resume + JD + instructions typically fits in 8 192;
    /// 16 384 gives headroom for verbose CVs. qwen3.5:4b supports up to 32 768.
    /// </summary>
    public int NumCtx { get; set; } = 16384;
    public int TimeoutSeconds { get; set; } = 120;
}
