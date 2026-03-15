using lucidRESUME.Core.Models.Extraction;

namespace lucidRESUME.Core.Interfaces;

public record DetectionContext(string Text, string? Markdown = null, int PageNumber = 1);

public interface IEntityDetector
{
    string DetectorId { get; }
    int Priority { get; }
    Task<IReadOnlyList<ExtractedEntity>> DetectAsync(DetectionContext context, CancellationToken ct = default);
}
