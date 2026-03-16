using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Extraction.Pipeline;
using lucidRESUME.Parsing;
using lucidRESUME.Parsing.Templates;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Ingestion.Parsing;

public sealed class ResumeParser : IResumeParser
{
    private readonly IDoclingClient _docling;
    private readonly ExtractionPipeline _extraction;
    private readonly IDocumentImageCache _imageCache;
    private readonly ParserSelector _parserSelector;
    private readonly TemplateRegistry _templateRegistry;
    private readonly ILlmExtractionService? _llm;
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(
        IDoclingClient docling,
        ExtractionPipeline extraction,
        IDocumentImageCache imageCache,
        ParserSelector parserSelector,
        TemplateRegistry templateRegistry,
        ILogger<ResumeParser> logger,
        ILlmExtractionService? llm = null)
    {
        _docling = docling;
        _extraction = extraction;
        _imageCache = imageCache;
        _parserSelector = parserSelector;
        _templateRegistry = templateRegistry;
        _llm = llm;
        _logger = logger;
    }

    public async Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var resume = ResumeDocument.Create(fileInfo.Name, GetContentType(fileInfo.Extension), fileInfo.Length);

        // ── 1. Try direct parse (no Docling round-trip) ───────────────────
        var direct = await _parserSelector.TryDirectParseAsync(filePath, ct);

        string markdown;
        string? plainText;
        IReadOnlyList<lucidRESUME.Parsing.DocumentSection>? structuredSections = null;

        if (direct is not null)
        {
            _logger.LogInformation("Using direct parse result for {File} (confidence={Confidence:P0}, template={Template})",
                fileInfo.Name, direct.Confidence, direct.TemplateName ?? "unknown");
            markdown = direct.Markdown;
            plainText = direct.PlainText;
            structuredSections = direct.Sections.Count > 0 ? direct.Sections : null;
            resume.SetDoclingOutput(markdown, null, plainText);
            resume.PageCount = direct.PageCount;

            // Learn this template if it was a confident anonymous parse (no prior match)
            if (direct.TemplateName is null && direct.Confidence >= 0.80
                && Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                var fingerprint = TemplateFingerprint.FromFile(filePath);
                if (fingerprint is not null)
                    _ = _templateRegistry.LearnAsync(fingerprint, fileInfo.Name, ct);
            }
        }
        else
        {
            // ── 2. Docling fallback ───────────────────────────────────────
            _logger.LogInformation("Converting {File} via Docling", fileInfo.Name);
            var docling = await _docling.ConvertAsync(filePath, ct);
            resume.SetDoclingOutput(docling.Markdown, docling.Json, docling.PlainText);
            markdown = docling.Markdown;
            plainText = docling.PlainText;

            // Cache page images — page 1 eagerly, rest fire-and-forget
            if (docling.PageImages.Count > 0)
            {
                var cacheKey = _imageCache.ComputeKey(filePath);
                resume.ImageCacheKey = cacheKey;
                resume.PageCount = docling.PageImages.Count;

                await _imageCache.StorePageAsync(cacheKey, 1, docling.PageImages[0], ct);

                if (docling.PageImages.Count > 1)
                    _ = CacheRemainingPagesAsync(cacheKey, docling.PageImages, ct);
            }
        }

        // ── 3. Entity extraction ──────────────────────────────────────────
        var context = new DetectionContext(plainText ?? markdown, markdown);
        var entities = await _extraction.RunAsync(context, ct);
        foreach (var entity in entities)
            resume.AddEntity(entity);

        MapEntitiesToSchema(resume, entities);

        // ── 4. Section parsing ────────────────────────────────────────────
        MarkdownSectionParser.PopulateSections(resume, markdown, structuredSections);

        // ── 5. LLM fallback for missing fields (fire-and-forget) ─────────
        // Non-blocking: doesn't slow down parse return, updates resume in background.
        // Results are available if the caller awaits resume.LlmEnhancementTask.
        if (_llm != null && (resume.Skills.Count == 0 || resume.Experience.Count == 0))
            resume.LlmEnhancementTask = LlmFillMissingAsync(resume, plainText ?? markdown, CancellationToken.None);

        return resume;
    }

    private async Task LlmFillMissingAsync(ResumeDocument resume, string text, CancellationToken ct)
    {
        // Skills: ask LLM only when rule-based extraction found nothing
        if (resume.Skills.Count == 0)
        {
            _logger.LogDebug("Skills empty — asking LLM for extraction");
            var raw = await _llm!.ExtractSkillsAsync(text, ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // Parse the comma-separated response into Skill objects
                foreach (var part in raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = part.Trim().TrimStart('-', '*', '•', ' ').Trim();
                    if (name.Length >= 2 && name.Length <= 50
                        && !name.Contains("experience", StringComparison.OrdinalIgnoreCase)
                        && name.Count(c => c == ' ') <= 5)
                        resume.Skills.Add(new lucidRESUME.Core.Models.Resume.Skill { Name = name });
                }
                if (resume.Skills.Count > 0)
                    _logger.LogInformation("LLM recovered {Count} skills", resume.Skills.Count);
            }
        }
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
