namespace lucidRESUME.AI;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.6-luna";
    public string ExtractionModel { get; set; } = "gpt-5.6-luna";
    public int MaxTokens { get; set; } = 8000;
    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
}
