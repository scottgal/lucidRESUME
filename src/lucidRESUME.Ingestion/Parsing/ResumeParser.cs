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
    private readonly IDoclingClient? _docling;
    private readonly ExtractionPipeline _extraction;
    private readonly IDocumentImageCache _imageCache;
    private readonly ParserSelector _parserSelector;
    private readonly TemplateRegistry _templateRegistry;
    private readonly ILlmExtractionService? _llm;
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(
        ExtractionPipeline extraction,
        IDocumentImageCache imageCache,
        ParserSelector parserSelector,
        TemplateRegistry templateRegistry,
        ILogger<ResumeParser> logger,
        IDoclingClient? docling = null,
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

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".txt"
    };

    public async Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var ext = fileInfo.Extension.ToLowerInvariant();

        if (!SupportedExtensions.Contains(ext))
            throw new NotSupportedException(
                $"Unsupported file type '{ext}'. Supported formats: {string.Join(", ", SupportedExtensions.Order())}");

        var resume = ResumeDocument.Create(fileInfo.Name, GetContentType(fileInfo.Extension), fileInfo.Length);

        // ── 1. Try direct parse (no Docling round-trip) ───────────────────
        var direct = await _parserSelector.TryDirectParseAsync(filePath, ct);

        string markdown;
        string? plainText;
        IReadOnlyList<lucidRESUME.Parsing.DocumentSection>? structuredSections = null;

        // For PDFs: prefer Docling when available (much better layout detection)
        // For DOCX: prefer direct parse (already high quality, no need for Docling)
        var isPdf = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var useDocling = isPdf && _docling is not null;

        if (useDocling)
        {
            // ── PDF + Docling: use ML-based layout detection ──────────────
            _logger.LogInformation("Converting PDF {File} via Docling (ML layout detection)", fileInfo.Name);
            var docling = await _docling!.ConvertAsync(filePath, ct);
            // Docling preserves tab characters from PDF layout — normalize to spaces
            var doclingMd = docling.Markdown?.Replace('\t', ' ') ?? "";
            var doclingText = docling.PlainText?.Replace('\t', ' ');
            resume.SetDoclingOutput(doclingMd, docling.Json, doclingText);
            markdown = doclingMd;
            plainText = doclingText;

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
        else if (direct is not null)
        {
            // ── DOCX or PDF without Docling: use direct parse ─────────────
            _logger.LogInformation("Using direct parse for {File} (confidence={Confidence:P0}, template={Template})",
                fileInfo.Name, direct.Confidence, direct.TemplateName ?? "unknown");
            markdown = direct.Markdown;
            plainText = direct.PlainText;
            structuredSections = direct.Sections.Count > 0 ? direct.Sections : null;
            resume.SetDoclingOutput(markdown, null, plainText);
            resume.PageCount = direct.PageCount;

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
            // ── 2b. No Docling, no direct parser — unsupported ───────────
            throw new NotSupportedException(
                $"Cannot parse '{fileInfo.Name}' without Docling. Enable Docling in settings or use a PDF/DOCX file.");
        }

        // ── 3. Entity extraction ──────────────────────────────────────────
        var context = new DetectionContext(plainText ?? markdown, markdown);
        var entities = await _extraction.RunAsync(context, ct);
        foreach (var entity in entities)
            resume.AddEntity(entity);

        MapEntitiesToSchema(resume, entities);
        InferNameIfMissing(resume, markdown);

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
                // Parse the comma-separated response into Skill objects, deduplicated, max 60
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (resume.Skills.Count >= 60) break;
                    var name = part.Trim().TrimStart('-', '*', '•', ' ').Trim();
                    if (name.Length >= 2 && name.Length <= 50
                        && !name.Contains("experience", StringComparison.OrdinalIgnoreCase)
                        && name.Count(c => c == ' ') <= 5
                        && seen.Add(name))
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

    /// <summary>
    /// Fallback: if NER didn't find a name, use the first non-empty line of the markdown
    /// (before any heading marker) as the person's name. Most resumes start with the name.
    /// </summary>
    private static void InferNameIfMissing(ResumeDocument resume, string markdown)
    {
        if (resume.Personal.FullName is not null) return;

        // Strategy 1: Use NER PersonName entity (most reliable)
        var nerName = resume.Entities
            .Where(e => e.Classification == "PersonName" && e.Value.Length > 3)
            .OrderByDescending(e => e.Confidence)
            .FirstOrDefault();
        if (nerName is not null)
        {
            resume.Personal.FullName = nerName.Value;
            return;
        }

        // Strategy 2: First non-heading, non-section line that looks like a name
        // (short, no numbers, not a known section keyword, no URLs)
        foreach (var rawLine in markdown.Split('\n'))
        {
            var text = rawLine.Trim('\r', ' ').TrimStart('#').Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > 60 || text.Length < 3) continue;
            if (text.Any(char.IsDigit)) continue; // skip addresses, phone numbers
            if (text.Contains('@') || text.Contains("http")) continue;
            if (Ingestion.Parsing.SectionClassifier.ClassifyHeading(text) is not null) continue;
            resume.Personal.FullName = text;
            return;
        }

        // Strategy 3: Fall back to first non-empty line (last resort)
        foreach (var rawLine in markdown.Split('\n'))
        {
            var text = rawLine.Trim('\r', ' ').TrimStart('#').Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            resume.Personal.FullName = text;
            break;
        }
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
