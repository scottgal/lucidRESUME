namespace lucidRESUME.Core.Interfaces;

/// <param name="PageImages">
/// Ordered list of raw PNG bytes for each page (1-based: index 0 = page 1).
/// Empty when Docling image export was not requested or returned no images.
/// </param>
public record DoclingConversionResult(
    string Markdown,
    string? Json,
    string? PlainText,
    IReadOnlyList<byte[]> PageImages);

public interface IDoclingClient
{
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
    Task<DoclingConversionResult> ConvertAsync(string filePath, CancellationToken ct = default);
}
