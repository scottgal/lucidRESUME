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
    private readonly Layout.DocumentLayoutDetector? _layoutDetector;
    private readonly ILlmExtractionService? _llm;
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(
        ExtractionPipeline extraction,
        IDocumentImageCache imageCache,
        ParserSelector parserSelector,
        TemplateRegistry templateRegistry,
        ILogger<ResumeParser> logger,
        IDoclingClient? docling = null,
        Layout.DocumentLayoutDetector? layoutDetector = null,
        ILlmExtractionService? llm = null)
    {
        _docling = docling;
        _extraction = extraction;
        _imageCache = imageCache;
        _parserSelector = parserSelector;
        _templateRegistry = templateRegistry;
        _layoutDetector = layoutDetector;
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
        InferName(resume, markdown);

        // ── 4. Section parsing ────────────────────────────────────────────
        MarkdownSectionParser.PopulateSections(resume, markdown, structuredSections);

        // ── 4b. Layout detection from page images
        // Uses DocLayNet YOLO model to detect document regions and build a structural hash.
        // If no cached images exist, renders via Morph for DOCX files.
        if (_layoutDetector is not null && mode != ParseMode.Fast)
        {
            try
            {
                string? pageImagePath = null;

                // Try cached page images first
                if (resume.ImageCacheKey is not null)
                    pageImagePath = _imageCache.GetCachedPagePath(resume.ImageCacheKey, 1);

                // If no cached image, render DOCX via Morph on the fly
                if (pageImagePath is null
                    && Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "lucidRESUME-layout", Path.GetFileNameWithoutExtension(filePath));
                    var converter = new global::WordRender.Skia.DocumentConverter();
                    var result = converter.ConvertToImages(filePath, tempDir, new global::WordRender.ConversionOptions
                    {
                        Dpi = 150,
                        FontWidthScale = 1.07,
                        FontFallback = _ => "Arial",
                    });
                    if (result.ImagePaths.Count > 0)
                        pageImagePath = result.ImagePaths[0];
                }

                if (pageImagePath is not null && File.Exists(pageImagePath))
                {
                    var regions = await _layoutDetector.DetectAsync(pageImagePath, ct);
                    if (regions.Count > 0)
                    {
                        var layoutHash = Layout.DocumentLayoutHash.Compute(regions);
                        _logger.LogInformation("Layout detection: {Count} regions, hash={Hash}", regions.Count, layoutHash);

                        // Log region summary for debugging
                        var summary = regions.GroupBy(r => r.Label)
                            .Select(g => $"{g.Key}:{g.Count()}")
                            .ToList();
                        _logger.LogDebug("Layout regions: {Summary}", string.Join(", ", summary));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Layout detection skipped");
            }
        }

        // ── 5. LLM fallback — only in AI/Full mode and when structural extraction has gaps
        // Fast mode: never calls LLM (sub-second parse, no external services)
        if (mode != ParseMode.Fast && _llm != null
            && (resume.Skills.Count == 0 || resume.Experience.Count == 0 || _nameConfidence < 0.50))
        {
            _logger.LogDebug("Structural parse gaps (skills={Skills}, exp={Exp}) — escalating to LLM",
                resume.Skills.Count, resume.Experience.Count);
            resume.LlmEnhancementTask = LlmFillMissingAsync(resume, plainText ?? markdown, ct);
        }

        return resume;
    }

    private async Task LlmFillMissingAsync(ResumeDocument resume, string text, CancellationToken ct)
    {
        // Name: ask LLM when RRF fusion confidence is low (< 0.50) or name is null
        if (_nameConfidence < 0.50 || resume.Personal.FullName is null)
        {
            // Send just the header (first 10 lines) — small, fast, cheap
            var headerLines = text.Split('\n').Take(10);
            var header = string.Join('\n', headerLines);
            _logger.LogDebug("Name confidence {Conf:P0} — asking LLM for extraction", _nameConfidence);
            var llmName = await _llm!.ExtractNameAsync(header, ct);
            if (!string.IsNullOrWhiteSpace(llmName) && llmName.Length >= 3 && llmName.Length <= 50)
            {
                // Normalise ALL CAPS LLM responses to title case
                resume.Personal.FullName = NormaliseName(llmName) ?? llmName;
                _logger.LogInformation("LLM recovered name: {Name}", resume.Personal.FullName);
            }
        }

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
        // PersonName is handled by InferNameIfMissing which does NER + markdown cross-referencing
        resume.Personal.LinkedInUrl = byClass.GetValueOrDefault("LinkedInUrl")?.Value;
        resume.Personal.GitHubUrl = byClass.GetValueOrDefault("GitHubUrl")?.Value;
    }

    /// <summary>
    /// RRF fusion name extraction: all signals produce candidates simultaneously,
    /// multi-source agreement boosts confidence. LLM backstop when confidence is low.
    /// Signals: NER entities, heading text, first line, email cross-reference, OCR fix.
    /// </summary>
    private void InferName(ResumeDocument resume, string markdown)
    {
        var candidates = new List<(string Name, double Confidence, string Source)>();
        var lines = markdown.Split('\n');

        // ── Signal 1: NER PersonName entities ────────────────────────────────
        foreach (var e in resume.Entities.Where(e => e.Classification == "PersonName" && e.Value.Length > 2))
        {
            var name = NormaliseName(e.Value);
            if (name is not null && !IsNonNamePhrase(name))
                candidates.Add((name, e.Confidence, "NER"));
        }

        // ── Signal 2: Markdown headings ──────────────────────────────────────
        foreach (var rawLine in lines.Take(30))
        {
            if (!rawLine.StartsWith('#')) continue;
            var heading = rawLine.TrimStart('#').Trim();
            if (heading.Length < 3 || IsDocumentTitle(heading)) continue;
            heading = CleanHeadingForName(heading);
            if (heading.Length < 3) continue;

            // Try OCR fix, concatenated split, email inference, title case, raw
            AddIfNameLike(candidates, TryFixOcrSpacing(heading), 0.70, "Heading-OCR");
            AddIfNameLike(candidates, TrySplitConcatenatedName(heading), 0.65, "Heading-Split");
            if (!heading.Contains(' ') && heading.Length >= 4 && heading.All(char.IsLetter))
                AddIfNameLike(candidates, TryInferNameFromEmail(resume, heading), 0.60, "Heading-Email");
            if (heading.Length > 3 && heading == heading.ToUpperInvariant())
                AddIfNameLike(candidates, ToTitleCase(heading), 0.55, "Heading-Caps");
            AddIfNameLike(candidates, heading, 0.60, "Heading");
        }

        // ── Signal 3: First meaningful lines (positional) ────────────────────
        foreach (var rawLine in lines.Take(5))
        {
            var text = rawLine.Trim('\r', ' ').TrimStart('#').Trim();
            if (text.Length < 3 || text.Length > 40) continue;
            if (text.Any(char.IsDigit) || text.Contains('@') || text.Contains("http")) continue;
            if (SectionClassifier.ClassifyHeading(text) is not null) continue;
            if (IsLikelyJobTitle(text.ToLowerInvariant())) continue;
            AddIfNameLike(candidates, text, 0.50, "FirstLine");
            AddIfNameLike(candidates, TryFixOcrSpacing(text), 0.50, "FirstLine-OCR");
        }

        if (candidates.Count == 0) return;

        // ── RRF fusion: group by normalised name, boost multi-source ─────────
        const double multiSourceBoost = 0.10;
        var fused = candidates
            .GroupBy(c => c.Name.ToLowerInvariant().Trim())
            .Select(g =>
            {
                var best = g.MaxBy(c => c.Confidence);
                var sources = g.Select(c => c.Source).Distinct().Count();
                var boosted = Math.Min(1.0, best.Confidence + multiSourceBoost * (sources - 1));
                return (best.Name, Confidence: boosted, Sources: sources);
            })
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.Sources)
            .ToList();

        // ── Cross-reference: if NER fragment matches a longer positional candidate, prefer the longer one
        var bestCandidate = fused[0];
        var nerFragments = fused.Where(c => candidates.Any(cc => cc.Name == c.Name && cc.Source == "NER")).ToList();
        foreach (var nf in nerFragments)
        {
            var longer = fused.FirstOrDefault(f =>
                f.Name != nf.Name
                && f.Name.StartsWith(nf.Name, StringComparison.OrdinalIgnoreCase)
                && IsNameLike(f.Name));
            if (longer.Name is not null)
            {
                bestCandidate = longer;
                break;
            }
        }

        resume.Personal.FullName = bestCandidate.Name;
        _nameConfidence = bestCandidate.Confidence;

        // Penalise confidence for obviously incomplete names (single word, too short, non-name phrases)
        if (!IsNameLike(bestCandidate.Name) || bestCandidate.Name.Length < 4 || IsNonNamePhrase(bestCandidate.Name))
            _nameConfidence = Math.Min(_nameConfidence, 0.30);
    }

    /// <summary>Confidence of the name extraction — used to decide if LLM backstop is needed.</summary>
    private double _nameConfidence;

    private static void AddIfNameLike(List<(string Name, double Confidence, string Source)> candidates,
        string? text, double confidence, string source)
    {
        if (text is null) return;
        text = NormaliseName(text);
        if (text is not null && IsNameLike(text) && !IsNonNamePhrase(text))
            candidates.Add((text, confidence, source));
    }

    /// <summary>Normalises ALL CAPS names to Title Case.</summary>
    private static string? NormaliseName(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return null;
        text = text.Trim();
        // If ALL CAPS and looks like a name, title-case it
        if (text.Length > 3 && text == text.ToUpperInvariant() && !text.Any(char.IsDigit))
            return ToTitleCase(text);
        return text;
    }

    /// <summary>
    /// Strips trailing phone numbers, email addresses, and separators from headings.
    /// "Luiza Maria CICONE H 07 81 36 81 44" → "Luiza Maria CICONE"
    /// </summary>
    private static string CleanHeadingForName(string heading)
    {
        // Strip everything after a phone-like sequence (digit runs)
        var result = heading;

        // Remove trailing content starting from first digit sequence that looks like a phone
        var digitRunStart = -1;
        for (var i = 0; i < result.Length; i++)
        {
            if (char.IsDigit(result[i]))
            {
                // Count consecutive digits (allowing spaces between)
                var digitCount = 0;
                for (var j = i; j < result.Length; j++)
                {
                    if (char.IsDigit(result[j])) digitCount++;
                    else if (result[j] != ' ' && result[j] != '-' && result[j] != '(' && result[j] != ')') break;
                }
                if (digitCount >= 4) // Phone number threshold
                {
                    digitRunStart = i;
                    break;
                }
            }
        }

        if (digitRunStart > 0)
        {
            // Walk back to trim any separator chars (H, T, |, etc)
            var end = digitRunStart;
            while (end > 0 && (result[end - 1] == ' ' || result[end - 1] == '|'
                || result[end - 1] == '-' || result[end - 1] == 'H' || result[end - 1] == 'T'))
                end--;
            result = result[..end].Trim();
        }

        return result;
    }

    /// <summary>
    /// Fixes OCR spacing artifacts where letters are separated by spaces.
    /// "F ELIPE M AGNO DE A LMEIDA" → "Felipe Magno De Almeida"
    /// Detects pattern: single uppercase letter followed by space and lowercase letters.
    /// </summary>
    private static string? TryFixOcrSpacing(string text)
    {
        // Pattern: alternating single-uppercase + space + lowercase run
        // "F ELIPE" = 'F' + ' ' + "ELIPE" or "F" + " " + "elipe"
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4) return null; // Need enough fragments to detect pattern

        // Count how many single-letter fragments there are
        var singleLetterCount = words.Count(w => w.Length == 1 && char.IsLetter(w[0]));
        if (singleLetterCount < 2) return null; // Not an OCR spacing issue

        // Merge: single letter + following fragment = one word
        var merged = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 1 && char.IsUpper(words[i][0]) && i + 1 < words.Length && words[i + 1].Length > 1)
            {
                merged.Add(words[i] + words[i + 1].ToLowerInvariant());
                i++; // Skip next
            }
            else
            {
                merged.Add(words[i]);
            }
        }

        if (merged.Count < 2 || merged.Count == words.Length) return null; // No merging happened

        return string.Join(' ', merged.Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLowerInvariant() : "") : w));
    }

    /// <summary>Checks if text looks like a person's name (2-5 capitalized words, no special chars).</summary>
    private static bool IsNameLike(string text)
    {
        if (text.Length < 3 || text.Length > 40) return false;
        // Names don't contain commas, semicolons, bullets, plus signs, brackets, pipes
        if (text.Any(c => c is ',' or ';' or '•' or '+' or '(' or ')' or '[' or ']' or '|' or '/' or ':')) return false;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Allow one initial (e.g. "D." in "Claud D. Park") but most words must be 2+ chars
        var shortWords = words.Count(w => w.Length < 2 || (w.Length == 2 && w[1] == '.'));
        return words.Length >= 2 && words.Length <= 5
            && shortWords <= 1 // At most one initial
            && words.All(w => w.Length >= 1 && char.IsUpper(w[0])
                && w.All(c => char.IsLetter(c) || c == '-' || c == '\'' || c == '.'));
    }

    /// <summary>
    /// Splits concatenated names where spaces were lost during PDF extraction.
    /// "petermüller" → "Peter Müller", "johndoe" → "John Doe"
    /// Detects uppercase transitions in the middle of a word.
    /// </summary>
    private static string? TrySplitConcatenatedName(string text)
    {
        if (text.Contains(' ') || text.Length < 4 || text.Length > 30) return null;
        if (text.Any(c => char.IsDigit(c) || c == '@' || c == '.')) return null;

        // Find uppercase transition points (handles Unicode: Müller → M is upper)
        var parts = new List<string>();
        var start = 0;
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]) && char.IsLower(text[i - 1]))
            {
                parts.Add(text[start..i]);
                start = i;
            }
        }
        parts.Add(text[start..]);

        if (parts.Count < 2) return null;

        // Capitalize each part
        var result = string.Join(' ', parts.Select(p =>
            char.ToUpper(p[0]) + p[1..]));
        return result;
    }

    /// <summary>
    /// Tries to split a concatenated heading into first+last name using email addresses.
    /// Scans both extracted entities and raw markdown for email patterns.
    /// e.g. heading="petermüller", email="peter@müller.com" → "Peter Müller"
    /// </summary>
    private static string? TryInferNameFromEmail(ResumeDocument resume, string heading)
    {
        // Collect all email candidates from entities and raw markdown
        var emails = new List<string>();
        if (resume.Personal.Email is not null)
            emails.Add(resume.Personal.Email);
        foreach (var e in resume.Entities.Where(e => e.Classification == "Email"))
            emails.Add(e.Value);

        // Also scan markdown for email patterns (handles Unicode domains like müller.com)
        if (resume.RawMarkdown is not null)
        {
            foreach (var line in resume.RawMarkdown.Split('\n'))
            {
                var idx = line.IndexOf('@');
                if (idx < 2) continue;
                // Walk backwards and forwards from @ to find the email
                var start = idx - 1;
                while (start > 0 && !char.IsWhiteSpace(line[start - 1])) start--;
                var end = idx + 1;
                while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
                var candidate = line[start..end].Trim();
                if (candidate.Contains('.') && candidate.Length > 5)
                    emails.Add(candidate);
            }
        }

        var headingLower = heading.ToLowerInvariant();
        foreach (var email in emails.Distinct())
        {
            if (!email.Contains('@')) continue;
            var localPart = email.Split('@')[0].ToLowerInvariant();
            var domain = email.Split('@')[1].Split('.')[0].ToLowerInvariant();

            // Pattern 1: peter@müller.com, heading=petermüller → first="peter", last="müller"
            if (headingLower.StartsWith(localPart) && headingLower.Length > localPart.Length)
            {
                var first = heading[..localPart.Length];
                var last = heading[localPart.Length..];
                if (first.Length >= 2 && last.Length >= 2)
                    return Capitalize(first) + " " + Capitalize(last);
            }

            // Pattern 2: peter@müller.com where domain matches second part of heading
            if (headingLower.EndsWith(domain) && headingLower.Length > domain.Length)
            {
                var first = heading[..^domain.Length];
                var last = heading[^domain.Length..];
                if (first.Length >= 2 && last.Length >= 2)
                    return Capitalize(first) + " " + Capitalize(last);
            }

            // Pattern 3: first.last@domain
            if (localPart.Contains('.'))
            {
                var parts = localPart.Split('.');
                if (parts.Length == 2 && parts[0].Length >= 2 && parts[1].Length >= 2
                    && headingLower == parts[0] + parts[1])
                    return Capitalize(parts[0]) + " " + Capitalize(parts[1]);
            }
        }

        return null;

        static string Capitalize(string s) => char.ToUpper(s[0]) + s[1..];
    }

    /// <summary>
    /// Rejects common phrases that look name-like (2 capitalized words) but aren't names.
    /// </summary>
    private static bool IsNonNamePhrase(string text)
    {
        var lower = text.ToLowerInvariant().Trim();
        return NonNamePhrases.Contains(lower) || IsLikelyJobTitle(lower);
    }

    /// <summary>Headings that are document titles, not person names.</summary>
    private static bool IsDocumentTitle(string text)
    {
        var lower = text.ToLowerInvariant().Trim();
        return lower is "resume" or "cv" or "curriculum vitae" or "résumé" or "lebenslauf"
            or "cover letter" or "reference" or "references" or "portfolio";
    }

    /// <summary>Converts "KISHAN KUMAR" to "Kishan Kumar".</summary>
    private static string ToTitleCase(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w =>
            w.Length <= 1 ? w : char.ToUpper(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static readonly HashSet<string> NonNamePhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "full time", "part time", "remote work", "hybrid work", "contract work",
        "cover letter", "curriculum vitae", "personal statement", "work experience",
        "work history", "career objective", "professional summary", "executive summary",
        "contact information", "contact details", "personal details", "personal information",
        "general business", "project management", "customer service", "data science",
        "machine learning", "deep learning", "natural language", "computer science",
        "information technology", "business administration", "human resources",
        "professional experience", "career summary", "career profile", "key skills",
        "core competencies", "technical skills", "soft skills", "additional information",
    };

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