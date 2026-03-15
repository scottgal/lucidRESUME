namespace lucidRESUME.Core.Interfaces;

public record DoclingConversionResult(string Markdown, string? Json, string? PlainText);

public interface IDoclingClient
{
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
    Task<DoclingConversionResult> ConvertAsync(string filePath, CancellationToken ct = default);
}
