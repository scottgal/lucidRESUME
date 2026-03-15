using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Extraction.Pipeline;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Ingestion.Parsing;

public sealed class ResumeParser : IResumeParser
{
    private readonly IDoclingClient _docling;
    private readonly ExtractionPipeline _extraction;
    private readonly IDocumentImageCache _imageCache;
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(
        IDoclingClient docling,
        ExtractionPipeline extraction,
        IDocumentImageCache imageCache,
        ILogger<ResumeParser> logger)
    {
        _docling = docling;
        _extraction = extraction;
        _imageCache = imageCache;
        _logger = logger;
    }

    public async Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var resume = ResumeDocument.Create(fileInfo.Name, GetContentType(fileInfo.Extension), fileInfo.Length);

        _logger.LogInformation("Converting {File} via Docling", fileInfo.Name);
        var docling = await _docling.ConvertAsync(filePath, ct);
        resume.SetDoclingOutput(docling.Markdown, docling.Json, docling.PlainText);

        var context = new DetectionContext(docling.PlainText ?? docling.Markdown, docling.Markdown);
        var entities = await _extraction.RunAsync(context, ct);
        foreach (var entity in entities)
            resume.AddEntity(entity);

        MapEntitiesToSchema(resume, entities);

        if (docling.Markdown is not null)
            MarkdownSectionParser.PopulateSections(resume, docling.Markdown);

        // Cache page images — page 1 eagerly, rest deferred to caller
        if (docling.PageImages.Count > 0)
        {
            var cacheKey = _imageCache.ComputeKey(filePath);
            resume.ImageCacheKey = cacheKey;
            resume.PageCount = docling.PageImages.Count;

            // Store page 1 synchronously so the UI can show it immediately
            await _imageCache.StorePageAsync(cacheKey, 1, docling.PageImages[0], ct);

            // Fire-and-forget the remaining pages
            if (docling.PageImages.Count > 1)
                _ = CacheRemainingPagesAsync(cacheKey, docling.PageImages, ct);
        }

        return resume;
    }

    private async Task CacheRemainingPagesAsync(string cacheKey, IReadOnlyList<byte[]> images, CancellationToken ct)
    {
        for (var i = 1; i < images.Count; i++)
        {
            try { await _imageCache.StorePageAsync(cacheKey, i + 1, images[i], ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to cache page {Page}", i + 1); }
        }
    }

    private static void MapEntitiesToSchema(ResumeDocument resume, IReadOnlyList<ExtractedEntity> entities)
    {
        resume.Personal.Email = entities.FirstOrDefault(e => e.Classification == "Email")?.Value;
        resume.Personal.Phone = entities.FirstOrDefault(e => e.Classification == "PhoneNumber")?.Value;
        resume.Personal.FullName = entities.FirstOrDefault(e => e.Classification == "PersonName" && e.Confidence > 0.85)?.Value;
        resume.Personal.LinkedInUrl = entities.FirstOrDefault(e => e.Classification == "LinkedInUrl")?.Value;
        resume.Personal.GitHubUrl = entities.FirstOrDefault(e => e.Classification == "GitHubUrl")?.Value;
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
