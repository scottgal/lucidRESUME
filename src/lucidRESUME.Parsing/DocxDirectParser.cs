using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using lucidRESUME.Parsing.Templates;

namespace lucidRESUME.Parsing;

/// <summary>
/// Parses .docx files directly via OpenXml without any HTTP call.
///
/// Heading detection strategy (in priority order):
///   1. Word built-in heading styles: "Heading 1" … "Heading 6"
///   2. Custom styles whose name contains "heading" (case-insensitive)
///   3. Bold + ALL-CAPS short paragraphs (common in resume section labels)
///
/// Template fingerprinting: if a <see cref="TemplateRegistry"/> is provided,
/// the document is matched against known templates. A match boosts confidence
/// and can unlock template-specific field mappings in future.
/// </summary>
public sealed class DocxDirectParser : IDocumentParser
{
    private readonly TemplateRegistry? _registry;

    public DocxDirectParser(TemplateRegistry? registry = null)
    {
        _registry = registry;
    }

    public IReadOnlyList<string> SupportedExtensions { get; } = [".docx"];

    public async Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
            if (doc.MainDocumentPart?.Document?.Body is null)
                return null;

            var body = doc.MainDocumentPart.Document.Body;
            var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;

            // ── Template fingerprint lookup ───────────────────────────────
            var fingerprint = TemplateFingerprint.FromDocument(doc);
            KnownTemplate? matchedTemplate = null;
            if (_registry is not null)
                matchedTemplate = await _registry.FindMatchAsync(fingerprint, ct);

            // ── Content extraction ────────────────────────────────────────
            var hints = matchedTemplate?.Hints;
            var sb = new StringBuilder();
            var plain = new StringBuilder();
            var sections = new List<DocumentSection>();
            DocumentSection? currentSection = null;
            var bodyBuf = new StringBuilder();

            foreach (var para in body.Descendants<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();

                var rawLines = GetParagraphLines(para);
                // For hints-based parsing, expand long flat lines that embed section keywords
                var lines = hints?.IsUsable == true
                    ? rawLines.SelectMany(l => SplitOnInlineHeadings(l, hints))
                    : rawLines;

                bool firstLine = true;
                foreach (var text in lines)
                {
                    int headingLevel;
                    string? semanticType = null;

                    if (hints?.IsUsable == true)
                    {
                        // After inline splitting, every segment can use section-map detection.
                        // Style-based check is only meaningful on the very first physical line.
                        (headingLevel, semanticType) = firstLine
                            ? DetectHeadingLevelFromHints(para, hints, text)
                            : DetectHeadingLevelFromHints(para, hints, text, styleCheckDisabled: true);
                    }
                    else
                    {
                        headingLevel = firstLine
                            ? DetectHeadingLevel(para, styles, text)
                            : DetectHeadingLevelTextOnly(text);
                    }

                    if (headingLevel > 0)
                    {
                        if (currentSection != null)
                            sections.Add(currentSection with { Body = bodyBuf.ToString().Trim() });
                        bodyBuf.Clear();
                        currentSection = new DocumentSection
                        {
                            Heading = text,
                            Level = headingLevel,
                            SemanticType = semanticType
                        };

                        var prefix = new string('#', headingLevel + 1); // h1→##, h2→###
                        sb.AppendLine($"{prefix} {text}");
                        plain.AppendLine(text);
                    }
                    else
                    {
                        sb.AppendLine(text);
                        plain.AppendLine(text);
                        bodyBuf.AppendLine(text);
                    }

                    firstLine = false;
                }
            }

            if (currentSection != null)
                sections.Add(currentSection with { Body = bodyBuf.ToString().Trim() });

            // ── Confidence ────────────────────────────────────────────────
            var baseConfidence = sections.Count > 0 ? 0.85 : 0.5;
            // Tuned templates with usable hints get a higher base confidence
            var hintBoost = hints?.IsUsable == true ? 0.10 : 0.0;
            var confidence = Math.Min(1.0, baseConfidence + hintBoost + (matchedTemplate?.ConfidenceBoost ?? 0));

            return new ParsedDocument
            {
                Markdown = sb.ToString(),
                PlainText = plain.ToString(),
                Sections = sections,
                PageCount = 0,
                Confidence = confidence,
                TemplateName = matchedTemplate?.Name
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Text extraction ──────────────────────────────────────────────────────

    /// <summary>
    /// Splits a paragraph's text on soft-return breaks (<w:br/>), returning
    /// each visual line as a separate string.  This handles the common resume
    /// template pattern where an entire sidebar column is a single &lt;w:p&gt;
    /// with its entries separated by Shift-Enter line breaks.
    /// </summary>
    private static IEnumerable<string> GetParagraphLines(Paragraph para)
    {
        var current = new StringBuilder();

        // Iterate all child elements — Runs contain plain text, Hyperlinks contain linked text
        foreach (var element in para.ChildElements)
        {
            IEnumerable<Run> runs = element switch
            {
                Run r => [r],
                Hyperlink h => h.Elements<Run>(),
                _ => []
            };

            foreach (var run in runs)
            {
                foreach (var child in run.ChildElements)
                {
                    if (child is Text t)
                        current.Append(t.InnerText);
                    else if (child is Break)
                    {
                        var seg = current.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(seg))
                            yield return seg;
                        current.Clear();
                    }
                }
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            yield return last;
    }

    /// <summary>
    /// For very long flat paragraphs (entire column in one &lt;w:t&gt; element),
    /// splits on known section keywords that appear in Title Case or ALL CAPS
    /// preceded by 2+ spaces or a sentence-ending period.
    /// Returns the original segment unchanged if no splits are found.
    /// </summary>
    // High-confidence section keywords that rarely appear in natural sentences.
    // Used for inline splitting of flat paragraphs to avoid false positives.
    private static readonly HashSet<string> HighConfidenceSectionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "experience", "education", "skills", "summary", "profile", "qualifications",
        "certifications", "certificates", "publications", "volunteer", "references",
        "languages", "interests", "awards", "achievements", "projects", "employment",
        "overview", "objective", "competencies", "expertise"
    };

    private static IEnumerable<string> SplitOnInlineHeadings(string text, TemplateParsingHints hints)
    {
        if (text.Length <= 200)
        {
            // If the whole text is itself a recognised section heading, never split it —
            // e.g. "SKILLS & EXPERTISE" must stay intact so heading detection works.
            if (hints.MapSection(text) != null)
            {
                yield return text;
                yield break;
            }

            // Short paragraphs: still check for a leading section keyword so that
            // e.g. "SUMMARY Main stack: PHP…" is split into heading + body.
            foreach (var part in SplitLeadingKeyword(text, hints))
                yield return part;
            yield break;
        }

        // Build alternation from HIGH-CONFIDENCE single-word section map keys only.
        // We avoid low-signal words like "stack", "tools", "honours" that often appear
        // in regular prose and cause false-positive splits.
        var singleWordKeys = hints.SectionMap.Keys
            .Where(k => !k.Contains(' ') && k.Length >= 4 && HighConfidenceSectionWords.Contains(k))
            .SelectMany(k => new[]
            {
                char.ToUpperInvariant(k[0]) + k[1..],  // Title Case
                k.ToUpperInvariant()                    // ALL CAPS
            })
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(k => k.Length)
            .ToList();

        if (singleWordKeys.Count == 0)
        {
            yield return text;
            yield break;
        }

        var altPattern = string.Join("|", singleWordKeys.Select(Regex.Escape));
        // Require a space (not just any non-word char like '-') before the keyword
        // to avoid false positives like "full-Stack" or "Honours degree".
        var re = new Regex($@"(?<![^\s])(?:{altPattern})(?!\w)", RegexOptions.Compiled);

        var splits = re.Matches(text)
            .Where(m => m.Index > 0)
            .Select(m => m.Index)
            .OrderBy(i => i)
            .ToList();

        if (splits.Count == 0)
        {
            foreach (var part in SplitLeadingKeyword(text, hints))
                yield return part;
            yield break;
        }

        // Yield segments; each (except possibly the first) starts with a keyword.
        var prev = 0;
        foreach (var idx in splits)
        {
            if (idx > prev)
            {
                var seg = text[prev..idx].Trim();
                if (!string.IsNullOrWhiteSpace(seg))
                    foreach (var part in SplitLeadingKeyword(seg, hints))
                        yield return part;
            }
            prev = idx;
        }
        var final = text[prev..].Trim();
        if (!string.IsNullOrWhiteSpace(final))
            foreach (var part in SplitLeadingKeyword(final, hints))
                yield return part;
    }

    /// <summary>
    /// If <paramref name="seg"/> starts with a known section keyword followed by a
    /// space and more content, yield the keyword alone first, then the remainder.
    /// Otherwise yield the segment as-is.
    /// </summary>
    private static IEnumerable<string> SplitLeadingKeyword(string seg, TemplateParsingHints hints)
    {
        // Try each single-word section map key (Title Case and ALL CAPS forms)
        foreach (var raw in hints.SectionMap.Keys.Where(k => !k.Contains(' ') && k.Length >= 4))
        {
            var titleCase = char.ToUpperInvariant(raw[0]) + raw[1..];
            var allCaps   = raw.ToUpperInvariant();

            foreach (var kw in new[] { titleCase, allCaps })
            {
                if (!seg.StartsWith(kw, StringComparison.Ordinal)) continue;
                if (seg.Length == kw.Length || char.IsWhiteSpace(seg[kw.Length]))
                {
                    var body = seg[kw.Length..].TrimStart();
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        yield return kw;
                        yield break;
                    }

                    // Don't split if body starts with a connector character — it's a compound
                    // heading like "SKILLS & EXPERTISE" or "EXPERIENCE / HISTORY".
                    if (body[0] == '&' || body[0] == '/' || body[0] == '|'
                        || body[0] == '-' || body[0] == '–' || body[0] == '—')
                    {
                        yield return seg;
                        yield break;
                    }

                    // Don't split if body is a single known section keyword — composite
                    // headings like "Skills summary" or "Work Experience" must stay intact.
                    // But DO split when body has further content: "EXPERIENCE Experience with APIs"
                    // is a section heading followed by body text, not a composite heading.
                    var bodyParts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (bodyParts.Length == 1)
                    {
                        var soloWord = bodyParts[0].TrimEnd(':', ',', ';', '.');
                        if (hints.SectionMap.ContainsKey(soloWord.ToLowerInvariant()))
                        {
                            yield return seg;
                            yield break;
                        }
                    }

                    yield return kw;
                    yield return body;
                    yield break;
                }
            }
        }

        yield return seg;
    }

    // ── Hint-based heading detection ─────────────────────────────────────────

    private static (int level, string? semanticType) DetectHeadingLevelFromHints(
        Paragraph para, TemplateParsingHints hints, string text, bool styleCheckDisabled = false)
    {
        // ── 1. Section map: deterministic signal from training data ───────────
        // If this exact text (or a prefix) is a known section heading, trust it.
        if (text.Length < 100)
        {
            var knownSemantic = hints.MapSection(text);
            if (knownSemantic != null)
                return (2, knownSemantic);
        }

        if (!styleCheckDisabled)
        {
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";

            // ── 2. Style-based role lookup ─────────────────────────────────────
            if (hints.NameStyleIds.Contains(styleId, StringComparer.OrdinalIgnoreCase))
                return (1, "Name");

            if (hints.SectionStyleIds.Contains(styleId, StringComparer.OrdinalIgnoreCase))
            {
                var semantic = hints.MapSection(text);
                return (2, semantic);
            }

            if (hints.SubSectionStyleIds.Contains(styleId, StringComparer.OrdinalIgnoreCase))
                return (3, null);
        }

        // ── 3. ALL-CAPS catch-all for headings not in training data ────────────
        var allCaps = text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter);
        if (allCaps)
        {
            var semantic = hints.MapSection(text);
            return (2, semantic);
        }

        return (0, null);
    }

    /// <summary>
    /// Heuristic heading detection for soft-return continuation lines where
    /// no paragraph style is available.  Only uses ALL-CAPS detection.
    /// </summary>
    private static int DetectHeadingLevelTextOnly(string text)
    {
        if (text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
            return 2;
        return 0;
    }

    // ── Heuristic heading detection ──────────────────────────────────────────

    private static int DetectHeadingLevel(Paragraph para, Styles? styles, string text)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId != null)
        {
            var lower = styleId.ToLowerInvariant();
            if (lower == "heading1" || lower == "1") return 1;
            if (lower == "heading2" || lower == "2") return 2;
            if (lower == "heading3" || lower == "3") return 3;

            if (styles != null)
            {
                var styleName = styles.Elements<Style>()
                    .FirstOrDefault(s => s.StyleId?.Value == styleId)
                    ?.StyleName?.Val?.Value ?? "";
                var sn = styleName.ToLowerInvariant();
                if (sn.Contains("heading") || sn.Contains("title") || sn.Contains("section"))
                    return sn.Contains("2") || sn.Contains("sub") ? 2 : 1;
            }
        }

        var isBold = para.Descendants<Bold>().Any();
        if (isBold && text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
            return 2;

        // Bold short text that matches a known section keyword = heading
        // Handles resumes that use bold title-case headings without Word heading styles
        if (isBold && text.Length < 60 && text.Any(char.IsLetter) && IsLikelySectionHeading(text))
            return 2;

        if (text.Length < 50 && text.Length > 3
            && text == text.ToUpperInvariant()
            && text.Any(char.IsLetter)
            && !text.Contains(','))
            return 2;

        return 0;
    }

    /// <summary>
    /// Detects likely section headings by checking if bold short text contains
    /// common resume section trigger words. No hardcoded list — uses substring
    /// matching against a small set of root tokens that appear in virtually all
    /// resume section headings across languages and formats.
    /// </summary>
    private static bool IsLikelySectionHeading(string text)
    {
        if (text.Length > 60 || text.Length < 3) return false;
        var lower = text.Trim().TrimEnd(':').ToLowerInvariant();

        // Root tokens that appear in virtually all resume section headings
        ReadOnlySpan<string> roots =
        [
            "summary", "experience", "education", "skill", "competenc",
            "project", "certification", "award", "language", "interest",
            "publication", "reference", "objective", "contact", "profile",
            "qualification", "expertise", "employment", "achievement",
        ];

        foreach (var root in roots)
        {
            if (lower.Contains(root))
                return true;
        }
        return false;
    }
}
