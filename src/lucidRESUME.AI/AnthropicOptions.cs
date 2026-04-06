namespace lucidRESUME.AI;

public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string ExtractionModel { get; set; } = "claude-haiku-4-5-20251001";
    public int MaxTokens { get; set; } = 8000;
    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
}
