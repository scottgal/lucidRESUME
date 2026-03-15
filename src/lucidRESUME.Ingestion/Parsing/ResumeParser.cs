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
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(IDoclingClient docling, ExtractionPipeline extraction, ILogger<ResumeParser> logger)
    {
        _docling = docling;
        _extraction = extraction;
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
        return resume;
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
