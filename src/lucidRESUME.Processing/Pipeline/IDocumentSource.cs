namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Provides raw content and metadata for a document entering the pipeline.
/// Implementations: FileDocumentSource, UrlDocumentSource, StreamDocumentSource.
/// </summary>
public interface IDocumentSource
{
    string SourceId { get; }
    string ContentType { get; }
    Task<Stream> OpenAsync(CancellationToken ct = default);
}
