using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// Analyses one or more sample documents that share the same template and
/// produces <see cref="TemplateParsingHints"/> automatically.
///
/// Algorithm:
///   1. Collect every (styleId → styleName, paragraphText) tuple across all samples.
///   2. Rank styles by frequency of "heading-like" paragraph content.
///   3. Assign role slots: name, section, sub-section, body.
///   4. Collect all unique heading texts → map to canonical section types.
/// </summary>
public static class TemplateHintsBuilder
{
    // ── Canonical section keyword map ─────────────────────────────────────────

    private static readonly Dictionary<string, string> SectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Experience variants
        ["experience"] = "Experience",
        ["work experience"] = "Experience",
        ["professional experience"] = "Experience",
        ["employment"] = "Experience",
        ["work history"] = "Experience",
        ["career history"] = "Experience",
        ["positions held"] = "Experience",
        ["experience summary"] = "Experience",
        ["employment history"] = "Experience",
        ["career summary"] = "Experience",
        ["relevant experience"] = "Experience",
        // Education
        ["education"] = "Education",
        ["academic"] = "Education",
        ["qualifications"] = "Education",
        ["degrees"] = "Education",
        ["education and training"] = "Education",
        ["academic background"] = "Education",
        ["educational background"] = "Education",
        ["education and courses"] = "Education",
        // Skills
        ["skills"] = "Skills",
        ["technical skills"] = "Skills",
        ["professional skills"] = "Skills",
        ["core competencies"] = "Skills",
        ["competencies"] = "Skills",
        ["expertise"] = "Skills",
        ["technologies"] = "Skills",
        ["stack"] = "Skills",
        ["skills summary"] = "Skills",
        ["key skills"] = "Skills",
        ["technologies & frameworks"] = "Skills",
        ["tools"] = "Skills",
        ["tools and technologies"] = "Skills",
        // Summary / Objective
        ["summary"] = "Summary",
        ["professional summary"] = "Summary",
        ["profile"] = "Summary",
        ["objective"] = "Summary",
        ["about"] = "Summary",
        ["overview"] = "Summary",
        ["executive summary"] = "Summary",
        ["personal statement"] = "Summary",
        ["career objective"] = "Summary",
        ["professional profile"] = "Summary",
        // Contact
        ["contact"] = "Contact",
        ["contact information"] = "Contact",
        ["personal details"] = "Contact",
        ["personal information"] = "Contact",
        ["contact details"] = "Contact",
        // Projects
        ["projects"] = "Projects",
        ["key projects"] = "Projects",
        ["notable projects"] = "Projects",
        ["personal projects"] = "Projects",
        // Certifications
        ["certifications"] = "Certifications",
        ["certificates"] = "Certifications",
        ["licenses"] = "Certifications",
        ["accreditations"] = "Certifications",
        // Languages
        ["languages"] = "Languages",
        ["languages knowledge"] = "Languages",
        // Publications / Awards
        ["publications"] = "Publications",
        ["awards"] = "Awards",
        ["achievements"] = "Awards",
        ["honours"] = "Awards",
        ["honors"] = "Awards",
        ["key achievements"] = "Awards",
        // References
        ["references"] = "References",
        // Volunteer
        ["volunteer"] = "Volunteer",
        ["volunteering"] = "Volunteer",
        ["community"] = "Volunteer",
        // Interests
        ["interests"] = "Interests",
        ["hobbies"] = "Interests",
        ["other"] = "Other",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds hints by analysing all .docx files in <paramref name="filePaths"/>.
    /// </summary>
    public static TemplateParsingHints BuildFromFiles(IEnumerable<string> filePaths)
    {
        var styleUsage = new Dictionary<string, StyleUsageStats>(StringComparer.OrdinalIgnoreCase);
        var observedSectionTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasTable = false;
        int sampleCount = 0;

        foreach (var path in filePaths)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(path, isEditable: false);
                if (doc.MainDocumentPart?.Document?.Body is null) continue;

                AnalyseDocument(doc, styleUsage, observedSectionTexts, ref hasTable);
                sampleCount++;
            }
            catch { /* skip unreadable files */ }
        }

        return BuildHints(styleUsage, observedSectionTexts, hasTable, sampleCount);
    }

    /// <summary>
    /// Refines existing hints by merging in analysis of additional files.
    /// </summary>
    public static TemplateParsingHints RefineHints(
        TemplateParsingHints existing, IEnumerable<string> filePaths)
    {
        var fresh = BuildFromFiles(filePaths);

        // Merge section maps
        foreach (var (k, v) in fresh.SectionMap)
            existing.SectionMap.TryAdd(k, v);

        // Merge style IDs (union)
        foreach (var id in fresh.NameStyleIds)
            if (!existing.NameStyleIds.Contains(id)) existing.NameStyleIds.Add(id);
        foreach (var id in fresh.SectionStyleIds)
            if (!existing.SectionStyleIds.Contains(id)) existing.SectionStyleIds.Add(id);
        foreach (var id in fresh.SubSectionStyleIds)
            if (!existing.SubSectionStyleIds.Contains(id)) existing.SubSectionStyleIds.Add(id);

        existing.SampleCount += fresh.SampleCount;
        existing.TunedAt = DateTimeOffset.UtcNow;
        return existing;
    }

    // ── Internal analysis ─────────────────────────────────────────────────────

    private static void AnalyseDocument(
        WordprocessingDocument doc,
        Dictionary<string, StyleUsageStats> styleUsage,
        HashSet<string> sectionTexts,
        ref bool hasTable)
    {
        var body = doc.MainDocumentPart!.Document.Body!;
        var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;

        if (body.Descendants<Table>().Any()) hasTable = true;

        var paragraphs = body.Elements<Paragraph>().ToList();
        bool seenFirstHeading = false;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var para = paragraphs[i];
            var text = GetText(para);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";
            var styleName = GetStyleName(styles, styleId);

            if (!styleUsage.TryGetValue(styleId, out var stats))
                styleUsage[styleId] = stats = new StyleUsageStats(styleId, styleName);

            stats.TotalCount++;

            // Classify the paragraph's role
            var role = ClassifyRole(para, styles, styleId, styleName, text, i, seenFirstHeading);
            stats.RoleCounts[role] = stats.RoleCounts.GetValueOrDefault(role) + 1;

            if (role is ParagraphRole.Name or ParagraphRole.Section or ParagraphRole.SubSection)
                seenFirstHeading = true;

            if (role == ParagraphRole.Section)
            {
                sectionTexts.Add(text.Trim());
            }
        }
    }

    private static TemplateParsingHints BuildHints(
        Dictionary<string, StyleUsageStats> styleUsage,
        HashSet<string> sectionTexts,
        bool hasTable,
        int sampleCount)
    {
        var hints = new TemplateParsingHints
        {
            UsesTableLayout = hasTable,
            SampleCount = sampleCount,
            TunedAt = DateTimeOffset.UtcNow,
        };

        // Assign style IDs by dominant role
        foreach (var stats in styleUsage.Values)
        {
            var dominantRole = stats.RoleCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            switch (dominantRole)
            {
                case ParagraphRole.Name:
                    hints.NameStyleIds.Add(stats.StyleId);
                    break;
                case ParagraphRole.Section:
                    hints.SectionStyleIds.Add(stats.StyleId);
                    break;
                case ParagraphRole.SubSection:
                    hints.SubSectionStyleIds.Add(stats.StyleId);
                    break;
                case ParagraphRole.Body:
                    hints.BodyStyleIds.Add(stats.StyleId);
                    break;
            }
        }

        // Pre-seed section map with all static keywords (comprehensive baseline)
        foreach (var (k, v) in SectionKeywords)
            hints.SectionMap.TryAdd(k, v);

        // Add observed heading texts from actual samples (may add new variants)
        foreach (var text in sectionTexts)
        {
            var canonical = MapToCanonical(text);
            if (canonical != null)
                hints.SectionMap.TryAdd(text.ToLowerInvariant().Trim(), canonical);
        }

        return hints;
    }

    // ── Role classification ───────────────────────────────────────────────────

    private static ParagraphRole ClassifyRole(
        Paragraph para, Styles? styles, string styleId, string styleName,
        string text, int index, bool seenFirstHeading)
    {
        var lower = styleId.ToLowerInvariant();
        var sn = styleName.ToLowerInvariant();

        // Name: first styled paragraph at doc top that looks like a person's name
        if (!seenFirstHeading && index <= 3)
        {
            if (lower is "title" or "heading1" or "1"
                || sn.Contains("name") || sn.Contains("title") || sn.Contains("heading 1"))
            {
                if (LooksLikeName(text)) return ParagraphRole.Name;
            }
        }

        // Section heading
        if (lower is "heading2" or "2"
            || sn.Contains("heading 2") || sn.Contains("section")
            || IsKnownSectionText(text))
        {
            if (text.Length < 80) return ParagraphRole.Section;
        }

        // Sub-section (job title / degree line)
        if (lower is "heading3" or "3"
            || sn.Contains("heading 3") || sn.Contains("subtitle"))
        {
            return ParagraphRole.SubSection;
        }

        // Bold short line at doc top = probably name
        if (!seenFirstHeading && index <= 2)
        {
            var isBold = para.Descendants<Bold>().Any();
            if (isBold && text.Length < 60 && LooksLikeName(text))
                return ParagraphRole.Name;
        }

        // Bold ALL-CAPS short line = section heading
        var allCaps = text.Length < 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter);
        if (allCaps) return ParagraphRole.Section;

        return ParagraphRole.Body;
    }

    private static bool LooksLikeName(string text)
    {
        // A name: 2-5 words, no digits, no colons/pipes
        if (text.Contains(':') || text.Contains('|') || text.Contains('@')) return false;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 6) return false;
        return words.All(w => w.Any(char.IsLetter));
    }

    private static bool IsKnownSectionText(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        return SectionKeywords.Keys.Any(k => lower == k || lower.StartsWith(k + " "));
    }

    private static string? MapToCanonical(string text)
    {
        var lower = text.Trim().ToLowerInvariant();

        // Exact match first
        if (SectionKeywords.TryGetValue(lower, out var exact)) return exact;

        // Prefix/contains match
        foreach (var (k, v) in SectionKeywords)
            if (lower.StartsWith(k) || lower.Contains(k))
                return v;

        return null;
    }

    // ── OpenXml helpers ───────────────────────────────────────────────────────

    private static string GetText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Elements<Run>())
            sb.Append(run.InnerText);
        return sb.ToString().Trim();
    }

    private static string GetStyleName(Styles? styles, string styleId)
    {
        if (styles == null) return styleId;
        return styles.Elements<Style>()
            .FirstOrDefault(s => s.StyleId?.Value == styleId)
            ?.StyleName?.Val?.Value ?? styleId;
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private enum ParagraphRole { Name, Section, SubSection, Body }

    private sealed class StyleUsageStats(string styleId, string styleName)
    {
        public string StyleId { get; } = styleId;
        public string StyleName { get; } = styleName;
        public int TotalCount { get; set; }
        public Dictionary<ParagraphRole, int> RoleCounts { get; } = new();
    }
}
