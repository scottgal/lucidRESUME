using Microsoft.Extensions.Logging;

namespace lucidRESUME.Parsing;

/// <summary>
/// Chooses between direct parsers (DOCX/PDF) and signals when Docling fallback
/// is needed.
///
/// Selection rules:
///  1. Find a parser that supports the file extension.
///  2. Attempt to parse - if result confidence &lt; <see cref="MinDirectConfidence"/>
///     treat as a Docling-fallback case.
///  3. If no direct parser exists, always fall back to Docling.
/// </summary>
public sealed class ParserSelector
{
    /// <summary>
    /// Minimum confidence from a direct parser before we trust it over Docling.
    /// Set lower to prefer direct parsing; higher to rely more on Docling.
    /// </summary>
    public double MinDirectConfidence { get; set; } = 0.5;

    private readonly IReadOnlyList<IDocumentParser> _parsers;
    private readonly ILogger<ParserSelector> _logger;

    public ParserSelector(IEnumerable<IDocumentParser> parsers, ILogger<ParserSelector> logger)
    {
        _parsers = parsers.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Attempts direct parsing. Returns null when Docling should be used instead.
    /// </summary>
    public async Task<ParsedDocument?> TryDirectParseAsync(string filePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var parser = _parsers.FirstOrDefault(p => p.SupportedExtensions.Contains(ext));
        if (parser is null)
        {
            _logger.LogDebug("No direct parser for {Ext} - using Docling", ext);
            return null;
        }

        _logger.LogDebug("Attempting direct parse of {File} with {Parser}",
            Path.GetFileName(filePath), parser.GetType().Name);

        var result = await parser.ParseAsync(filePath, ct);
        if (result is null || result.Confidence < MinDirectConfidence)
        {
            _logger.LogInformation(
                "Direct parse confidence {Confidence:P0} below threshold - falling back to Docling",
                result?.Confidence ?? 0);
            return null;
        }

        _logger.LogInformation("Direct parse succeeded for {File} (confidence={Confidence:P0})",
            Path.GetFileName(filePath), result.Confidence);
        return result;
    }
}