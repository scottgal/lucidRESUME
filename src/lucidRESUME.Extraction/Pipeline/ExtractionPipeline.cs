using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;

namespace lucidRESUME.Extraction.Pipeline;

public sealed class ExtractionPipeline
{
    private readonly IEnumerable<IEntityDetector> _detectors;

    public ExtractionPipeline(IEnumerable<IEntityDetector> detectors)
        => _detectors = detectors.OrderBy(d => d.Priority);

    public async Task<IReadOnlyList<ExtractedEntity>> RunAsync(DetectionContext context, CancellationToken ct = default)
    {
        var all = new List<ExtractedEntity>();
        foreach (var detector in _detectors)
        {
            var found = await detector.DetectAsync(context, ct);
            all.AddRange(found);
        }
        return all.AsReadOnly();
    }
}
