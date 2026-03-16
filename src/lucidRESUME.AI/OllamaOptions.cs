namespace lucidRESUME.AI;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    /// <summary>Small fast model for clarification / field extraction fallback.</summary>
    public string ExtractionModel { get; set; } = "qwen3.5:0.8b";
    public int TimeoutSeconds { get; set; } = 120;
}
