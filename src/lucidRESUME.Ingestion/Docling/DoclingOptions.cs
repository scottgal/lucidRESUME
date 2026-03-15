namespace lucidRESUME.Ingestion.Docling;

public sealed class DoclingOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5001";
    public int PollingIntervalMs { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 120;
}
