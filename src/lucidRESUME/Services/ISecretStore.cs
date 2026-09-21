namespace lucidRESUME.Services;

public interface ISecretStore
{
    bool IsAvailable { get; }
    string BackendName { get; }
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public static class AiSecretNames
{
    public const string OpenAiApiKey = "openai-api-key";
    public const string AnthropicApiKey = "anthropic-api-key";
    public const string JevApiKey = "jev-api-key";
}
