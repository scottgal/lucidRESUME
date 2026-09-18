namespace lucidRESUME.AI;

public sealed class LlamaSharpOptions
{
    public string ModelId { get; set; } = "ProCreations/grug-9b-gguf:Q4_K_M";
    public string ModelPath { get; set; } = "models/grug-9b-Q4_K_M.gguf";
    public string DownloadUrl { get; set; } =
        "https://huggingface.co/ProCreations/grug-9b-gguf/resolve/main/grug-9b-Q4_K_M.gguf";
    public uint ContextSize { get; set; } = 16_384;
    public int GpuLayerCount { get; set; } = -1;
    public int MaxTokens { get; set; } = 8_000;
    public int TimeoutSeconds { get; set; } = 600;
    public float Temperature { get; set; } = 0.2f;
}
