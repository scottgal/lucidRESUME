using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Extraction.Pipeline;

public sealed class ExtractionPipeline
{
    private readonly IEnumerable<IEntityDetector> _detectors;
    private readonly ILogger<ExtractionPipeline>? _logger;

    public ExtractionPipeline(IEnumerable<IEntityDetector> detectors,
        ILogger<ExtractionPipeline>? logger = null)
    {
        _detectors = detectors.OrderBy(d => d.Priority);
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExtractedEntity>> RunAsync(DetectionContext context, CancellationToken ct = default)
    {
        var all = new List<ExtractedEntity>();

        foreach (var detector in _detectors)
        {
            try
            {
                var found = await detector.DetectAsync(context, ct);
                all.AddRange(found);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Detector {Id} failed, continuing with others", detector.DetectorId);
            }
        }

        // Deduplicate: same (Classification, NormalizedValue) from multiple detectors
        // Keep the highest-confidence version
        var deduplicated = all
            .GroupBy(e => (e.Classification, e.NormalizedValue))
            .Select(g => g.OrderByDescending(e => e.Confidence).First())
            .ToList();

        return deduplicated.AsReadOnly();
    }
}
