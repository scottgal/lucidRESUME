namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Exports a populated schema to an output format (docx, pdf, json, markdown, …).
/// </summary>
public interface IDocumentExporter<TSchema> where TSchema : class
{
    string FormatId { get; }
    string ContentType { get; }

    Task<ExportResult> ExportAsync(TSchema schema, ExportOptions options, CancellationToken ct = default);
}

public sealed record ExportOptions(
    string OutputPath,
    string? TemplatePath = null,
    IReadOnlyDictionary<string, object>? Parameters = null
);

public sealed record ExportResult(
    string OutputPath,
    string ContentType,
    long SizeBytes
);
