using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Parsing;

/// <summary>
/// Parses a document file directly into a <see cref="ParsedDocument"/> without
/// requiring the Docling HTTP service. Implementations cover DOCX and PDF.
/// </summary>
public interface IDocumentParser
{
    /// <summary>File extensions this parser handles, e.g. ".docx".</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Parse the file and return structured content.
    /// Returns null when the parser cannot handle this specific file
    /// (e.g. password-protected, corrupt, format not recognized).
    /// </summary>
    Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
/// Raw content extracted by a direct parser, prior to entity detection.
/// </summary>
public sealed class ParsedDocument
{
    /// <summary>Full plain text, preserving paragraph breaks.</summary>
    public string PlainText { get; init; } = string.Empty;

    /// <summary>
    /// Markdown-ish representation with heading levels inferred from styles/font sizes.
    /// Used by <see cref="lucidRESUME.Ingestion.Parsing.MarkdownSectionParser"/>.
    /// </summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>Ordered sections detected by the parser.</summary>
    public List<DocumentSection> Sections { get; init; } = [];

    /// <summary>Number of pages (0 if unknown).</summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Confidence that the document was fully and correctly parsed.
    /// 1.0 = matched a known template exactly.
    /// 0.5 = structural heuristics only.
    /// 0.0 = could not parse — caller should fall back to Docling.
    /// </summary>
    public double Confidence { get; init; } = 0.5;

    /// <summary>Name of the matched known template, or null if none matched.</summary>
    public string? TemplateName { get; init; }
}

public record DocumentSection
{
    public string Heading { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int Level { get; init; } = 1;
}
