namespace lucidRESUME.Ingestion.Docling;

public sealed class DoclingOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5001";
    public int PollingIntervalMs { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 120;

    // Cloud mode — set UseCloud = true to use the Docling cloud API
    public bool UseCloud { get; set; } = false;
    public string CloudBaseUrl { get; set; } = "https://docling-serve.eu-de.direct.cloud.ibm.com";
    public string? CloudApiKey { get; set; }

    public string EffectiveBaseUrl => UseCloud ? CloudBaseUrl : BaseUrl;
}
