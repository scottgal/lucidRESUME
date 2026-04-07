using Microsoft.Extensions.Logging;
using WordRender;

namespace lucidRESUME.Ingestion.Preview;

/// <summary>
/// Generates page images from DOCX files using Morph (pure C#, cross-platform, no LibreOffice).
/// Falls back gracefully if rendering fails — Morph is v0.1.0 and may not handle all documents.
/// </summary>
public sealed class MorphDocxPreviewService
{
    private readonly ILogger<MorphDocxPreviewService> _logger;

    public MorphDocxPreviewService(ILogger<MorphDocxPreviewService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Render a DOCX file to PNG images, one per page.
    /// Returns file paths to generated PNGs, or empty array on failure.
    /// </summary>
    public Task<string[]> RenderToImagesAsync(string docxPath, string outputDir, CancellationToken ct = default)
    {
        if (!File.Exists(docxPath)) return Task.FromResult(Array.Empty<string>());

        var ext = Path.GetExtension(docxPath);
        if (!ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Array.Empty<string>());

        try
        {
            var converter = new global::WordRender.Skia.DocumentConverter();
            var options = new global::WordRender.ConversionOptions
            {
                Dpi = 150,
                FontWidthScale = 1.07, // better match to Word rendering
                FontFallback = fontName =>
                {
                    // Fallback chain for common resume fonts
                    _logger.LogDebug("Font not found: {Font}, using fallback", fontName);
                    return "Arial";
                }
            };

            var result = converter.ConvertToImages(docxPath, outputDir, options);

            if (result.PageCount > 0)
                _logger.LogInformation("Morph rendered {Count} pages from {File}", result.PageCount, Path.GetFileName(docxPath));

            return Task.FromResult(result.ImagePaths.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Morph failed to render {File} — will fall back to other preview methods",
                Path.GetFileName(docxPath));
            return Task.FromResult(Array.Empty<string>());
        }
    }
}
