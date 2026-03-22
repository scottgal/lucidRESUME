namespace lucidRESUME.AI;

public sealed class EmbeddingOptions
{
    /// <summary>"onnx" (default, local) or "ollama" (requires running Ollama service)</summary>
    public string Provider { get; set; } = "onnx";
    public string OnnxModelPath { get; set; } = "models/all-MiniLM-L6-v2.onnx";
    public string VocabPath { get; set; } = "models/vocab.txt";
}
