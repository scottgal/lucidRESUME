using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Extraction.Pipeline;
using lucidRESUME.Extraction.Recognizers;
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

    public Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default) =>
        ParseAsync(filePath, ParseMode.AI, ct);

    public async Task<ResumeDocument> ParseAsync(string filePath, ParseMode mode, CancellationToken ct = default)
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

        // Fast-first pipeline: try direct parse, only escalate to Docling if confidence is low.
        // DOCX: direct parse is already excellent (often 100% with template learning).
        // PDF: direct parse via PdfPig; escalate to Docling only when confidence < 70%.
        var isPdf = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var directConfidence = direct?.Confidence ?? 0;
        // Docling: only in Full mode, or AI mode when direct parse confidence is very low
        var needsDocling = isPdf && _docling is not null
            && (mode == ParseMode.Full || (mode == ParseMode.AI && directConfidence < 0.50));

        if (needsDocling)
        {
            // ── PDF with low-confidence direct parse: escalate to Docling ──
            _logger.LogInformation("Converting PDF {File} via Docling (direct parse confidence={Confidence:P0})",
                fileInfo.Name, directConfidence);
            var docling = await _docling!.ConvertAsync(filePath, ct);
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
            // ── 2b. No Docling, no direct parser - unsupported ───────────
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

        // ── 5. LLM fallback — only in AI/Full mode and when structural extraction has gaps
        // Fast mode: never calls LLM (sub-second parse, no external services)
        if (mode != ParseMode.Fast && _llm != null
            && (resume.Skills.Count == 0 || resume.Experience.Count == 0))
        {
            _logger.LogDebug("Structural parse gaps (skills={Skills}, exp={Exp}) — escalating to LLM",
                resume.Skills.Count, resume.Experience.Count);
            resume.LlmEnhancementTask = LlmFillMissingAsync(resume, plainText ?? markdown, ct);
        }

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

        // Experience: ask LLM when structural parser found nothing
        if (resume.Experience.Count == 0)
        {
            _logger.LogDebug("Experience empty — asking LLM for extraction");
            var raw = await _llm!.ExtractExperienceSummaryAsync(text, ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                const int maxEntries = 20; // cap to prevent LLM repetition loops

                foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (resume.Experience.Count >= maxEntries) break;

                    var trimmed = line.Trim().TrimStart('-', '*', '•', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ');
                    if (trimmed.Length < 5) continue;

                    // Dedup: skip if we've seen this exact line (LLM repetition)
                    if (!seen.Add(trimmed)) continue;

                    // Expected: "COMPANY_NAME | JOB_TITLE | START_DATE - END_DATE"
                    var parts = trimmed.Split('|', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 2)
                    {
                        // Detect if first part looks like a date (LLM sometimes reverses order)
                        var firstIsDate = ResumeDateParser.ContainsDate(parts[0]) && !ResumeDateParser.ContainsDate(parts[1]);
                        var company = firstIsDate ? (parts.Length >= 3 ? parts[2].Trim() : "Unknown") : parts[0].Trim();
                        var title = firstIsDate ? parts[1].Trim() : (parts.Length >= 2 ? parts[1].Trim() : "Unknown");
                        var datePart = firstIsDate ? parts[0].Trim() : (parts.Length >= 3 ? parts[2].Trim() : null);

                        // Skip if company == title (LLM confusion)
                        if (string.Equals(company, title, StringComparison.OrdinalIgnoreCase))
                            company = "Unknown";

                        var entry = new lucidRESUME.Core.Models.Resume.WorkExperience
                        {
                            Company = company,
                            Title = title
                        };

                        if (datePart != null)
                        {
                            var dateRange = ResumeDateParser.ExtractFirstDateRange(datePart);
                            if (dateRange != null)
                            {
                                entry.StartDate = dateRange.Start;
                                entry.EndDate = dateRange.End;
                                entry.IsCurrent = dateRange.IsCurrent;
                            }
                        }

                        resume.Experience.Add(entry);
                    }
                    else if (trimmed.Contains(',') || trimmed.Contains(" at "))
                    {
                        var sep = trimmed.Contains(" at ") ? " at " : ", ";
                        var idx = trimmed.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                        if (idx > 0)
                        {
                            resume.Experience.Add(new lucidRESUME.Core.Models.Resume.WorkExperience
                            {
                                Title = trimmed[..idx].Trim(),
                                Company = trimmed[(idx + sep.Length)..].Trim()
                            });
                        }
                    }
                }
                if (resume.Experience.Count > 0)
                    _logger.LogInformation("LLM recovered {Count} experience entries", resume.Experience.Count);
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
        // Single pass: build lookup by classification (avoids 5x linear scans)
        var byClass = new Dictionary<string, ExtractedEntity>();
        foreach (var e in entities)
        {
            if (!byClass.ContainsKey(e.Classification) || e.Confidence > byClass[e.Classification].Confidence)
                byClass[e.Classification] = e;
        }

        resume.Personal.Email = byClass.GetValueOrDefault("Email")?.Value;
        resume.Personal.Phone = byClass.GetValueOrDefault("PhoneNumber")?.Value;
        if (byClass.TryGetValue("PersonName", out var nameEntity) && nameEntity.Confidence > 0.85)
            resume.Personal.FullName = nameEntity.Value;
        resume.Personal.LinkedInUrl = byClass.GetValueOrDefault("LinkedInUrl")?.Value;
        resume.Personal.GitHubUrl = byClass.GetValueOrDefault("GitHubUrl")?.Value;
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

        // Strategies 2+3: scan markdown lines once
        var lines = markdown.Split('\n');
        string? fallback = null;

        foreach (var rawLine in lines)
        {
            var text = rawLine.Trim('\r', ' ').TrimStart('#').Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Track first non-empty line as last-resort fallback
            fallback ??= text;

            // Strategy 2: non-heading, non-section, name-like line
            if (text.Length > 40 || text.Length < 3) continue;
            if (text.Any(char.IsDigit)) continue;
            if (text.Contains('@') || text.Contains("http")) continue;

            // Skip section headers
            if (Ingestion.Parsing.SectionClassifier.ClassifyHeading(text) is not null) continue;

            // Skip ALL CAPS (likely job titles: "SALES MANAGER", "SOUS CHEF", "IT MANAGEMENT")
            if (text.Length > 5 && text == text.ToUpperInvariant()) continue;

            // Skip lines with common job title words
            var lower = text.ToLowerInvariant();
            if (IsLikelyJobTitle(lower)) continue;

            // Looks like a name: 2-4 words, mixed case, no special chars
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && words.Length <= 5
                && words.All(w => w.Length >= 2 && char.IsUpper(w[0])))
            {
                resume.Personal.FullName = text;
                return;
            }
        }

        // Strategy 3: first non-empty line (last resort) — but clean it up
        if (fallback is not null && fallback.Length <= 40)
            resume.Personal.FullName = fallback;
    }

    private static readonly HashSet<string> JobTitleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "manager", "director", "engineer", "developer", "analyst", "consultant",
        "specialist", "coordinator", "administrator", "supervisor", "assistant",
        "associate", "technician", "officer", "lead", "senior", "junior",
        "chef", "teacher", "nurse", "accountant", "designer", "architect",
        "intern", "instructor", "professor", "receptionist", "clerk", "agent",
        "representative", "executive", "president", "vice", "chief",
        "head", "principal", "superintendent", "foreman", "captain"
    };

    private static bool IsLikelyJobTitle(string lower)
    {
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(w => JobTitleKeywords.Contains(w));
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}