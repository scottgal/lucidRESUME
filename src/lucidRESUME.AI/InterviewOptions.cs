namespace lucidRESUME.AI;

public sealed class InterviewOptions
{
    public bool Enabled { get; set; }
    /// <summary>ollama or cloudflare. Disabled uses local rules only.</summary>
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nimble";
    public string CloudflareAccountId { get; set; } = "";
    /// <summary>Supply via environment/user secrets, never a committed settings file.</summary>
    public string ApiKey { get; set; } = "";
    public int TimeoutMilliseconds { get; set; } = 5000;
    public double MinimumProbability { get; set; } = .80;
    public double MinimumMargin { get; set; } = .20;
}
