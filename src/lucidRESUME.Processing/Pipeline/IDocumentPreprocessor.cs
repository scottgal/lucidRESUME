namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Converts a raw document into normalised text/markdown suitable for extraction.
/// Implementations: DoclingPreprocessor, PlainTextPreprocessor, HtmlPreprocessor.
/// </summary>
public interface IDocumentPreprocessor
{
    /// <summary>Supported MIME types (e.g. "application/pdf").</summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    Task<PreprocessedDocument> ProcessAsync(IDocumentSource source, CancellationToken ct = default);
}

public sealed record PreprocessedDocument(
    string SourceId,
    string PlainText,
    string? Markdown = null,
    string? StructuredJson = null,
    IReadOnlyDictionary<string, object>? Metadata = null
);
