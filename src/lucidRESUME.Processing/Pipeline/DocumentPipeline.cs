using Microsoft.Extensions.Logging;

namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Orchestrates the generalizable document processing pipeline:
///   Source → Preprocess → Extract → Map → [Compare] → Export
///
/// This is the domain-agnostic engine. Bind your TSchema-specific
/// preprocessor, extractors, mapper, and exporters via DI.
/// </summary>
public sealed class DocumentPipeline<TSchema> where TSchema : class, new()
{
    private readonly IDocumentPreprocessor _preprocessor;
    private readonly IReadOnlyList<IEntityExtractor> _extractors;
    private readonly ISchemaMapper<TSchema> _mapper;
    private readonly ILogger<DocumentPipeline<TSchema>> _logger;

    public DocumentPipeline(
        IDocumentPreprocessor preprocessor,
        IEnumerable<IEntityExtractor> extractors,
        ISchemaMapper<TSchema> mapper,
        ILogger<DocumentPipeline<TSchema>> logger)
    {
        _preprocessor = preprocessor;
        _extractors = [.. extractors.OrderBy(e => e.Priority)];
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PipelineResult<TSchema>> RunAsync(
        IDocumentSource source,
        TSchema? existingSchema = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Pipeline starting for source {SourceId}", source.SourceId);

        var document = await _preprocessor.ProcessAsync(source, ct);
        _logger.LogDebug("Preprocessed {SourceId}: {Chars} chars", source.SourceId, document.PlainText.Length);

        var allEntities = new List<DocumentEntity>();
        foreach (var extractor in _extractors)
        {
            var entities = await extractor.ExtractAsync(document, ct);
            _logger.LogDebug("Extractor {Id} found {Count} entities", extractor.ExtractorId, entities.Count);
            allEntities.AddRange(entities);
        }

        var schema = await _mapper.MapAsync(allEntities.AsReadOnly(), document, existingSchema, ct);

        _logger.LogInformation("Pipeline complete for {SourceId}: {EntityCount} entities", source.SourceId, allEntities.Count);
        return new PipelineResult<TSchema>(schema, allEntities.AsReadOnly(), document);
    }
}

public sealed record PipelineResult<TSchema>(
    TSchema Schema,
    IReadOnlyList<DocumentEntity> Entities,
    PreprocessedDocument Document
) where TSchema : class;
