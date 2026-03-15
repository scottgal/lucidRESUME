namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Maps extracted entities onto a target schema (TSchema).
/// Each domain defines its own schema and implements a mapper for it.
/// </summary>
public interface ISchemaMapper<TSchema> where TSchema : class, new()
{
    /// <summary>
    /// Map extracted entities onto an (optionally pre-populated) schema instance.
    /// </summary>
    Task<TSchema> MapAsync(
        IReadOnlyList<DocumentEntity> entities,
        PreprocessedDocument document,
        TSchema? existing = null,
        CancellationToken ct = default);
}
