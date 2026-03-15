namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Extracts typed entities from preprocessed document text.
/// Multiple extractors run in priority order; results are merged.
/// </summary>
public interface IEntityExtractor
{
    string ExtractorId { get; }
    int Priority { get; }

    Task<IReadOnlyList<DocumentEntity>> ExtractAsync(PreprocessedDocument document, CancellationToken ct = default);
}

public sealed record DocumentEntity(
    string Value,
    string EntityType,
    string Source,
    double Confidence,
    int? PageNumber = null,
    IReadOnlyDictionary<string, object>? Attributes = null
);
