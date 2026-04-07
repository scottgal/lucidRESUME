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

        // RRF fusion: same (Classification, NormalizedValue) from multiple detectors
        // gets a confidence boost — multi-source agreement is strong evidence.
        // Same pattern as JdFieldFuser for JD extraction.
        const double multiSourceBoost = 0.08;
        var fused = all
            .GroupBy(e => (e.Classification, Norm: e.NormalizedValue.ToLowerInvariant()))
            .Select(g =>
            {
                var best = g.OrderByDescending(e => e.Confidence).First();
                var distinctSources = g.Select(e => e.Source).Distinct().Count();
                if (distinctSources > 1)
                {
                    // Boost confidence for multi-source agreement
                    best.Confidence = Math.Min(1.0, best.Confidence + multiSourceBoost * (distinctSources - 1));
                }
                return best;
            })
            .ToList();

        return fused.AsReadOnly();
    }
}
