using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Extraction.Recognizers;
using lucidRESUME.Parsing;

namespace lucidRESUME.Ingestion.Parsing;

/// <summary>
/// Parses structured resume sections (Experience, Education, Skills) out of
/// the Docling-generated markdown output. Uses heuristics rather than strict
/// schema since Docling's markdown is document-layout-dependent.
///
/// Date extraction uses Microsoft.Recognizers.Text.DateTime (rule-based, offline)
/// rather than hand-crafted regex — handles all common date formats including
/// "Jan 2020 – Present", "2019–2022", "2020 - now", "October 2018 to date".
/// </summary>
public static class MarkdownSectionParser
{
    /// <summary>
    /// Populate resume sections from pre-parsed <see cref="DocumentSection"/> objects
    /// (produced by the direct DOCX/PDF parser).  When sections carry a
    /// <see cref="DocumentSection.SemanticType"/> from template hints, classification
    /// is skipped entirely — extraction becomes deterministic.
    /// Falls back to heuristic markdown parsing when sections are null/empty.
    /// </summary>
    public static void PopulateSections(
        ResumeDocument resume, string markdown, IReadOnlyList<DocumentSection>? sections = null)
    {
        // ── Fast path: use pre-classified sections from template hints ────────
        if (sections is { Count: > 0 } && sections.Any(s => s.SemanticType != null))
        {
            PopulateSectionsFromStructured(resume, sections);
            // Still run name extraction from markdown as a guard
            ExtractNameFromMarkdown(resume, markdown.Split('\n'));
            return;
        }

        // ── Standard heuristic path ───────────────────────────────────────────
        var lines = markdown.Split('\n');

        // ── 1. Extract summary: first non-heading paragraph ──────────────────
        var summary = ExtractSummary(lines);
        if (!string.IsNullOrWhiteSpace(summary) && resume.Personal.Summary == null)
            resume.Personal.Summary = summary;

        // ── 2. Extract full name from first heading (if not already set) ─────
        ExtractNameFromMarkdown(resume, lines);

        // ── 3. Split into labelled sections ──────────────────────────────────
        var labelledSections = SplitIntoSections(lines);

        // ── 4. Parse Skills ───────────────────────────────────────────────────
        if (resume.Skills.Count == 0)
        {
            var skillsContent = labelledSections.Where(s => s.Label == "Skills")
                                        .SelectMany(s => s.Lines)
                                        .ToList();
            if (skillsContent.Count > 0)
                ParseSkills(resume, skillsContent);
        }

        // ── 5. Parse Experience ───────────────────────────────────────────────
        if (resume.Experience.Count == 0)
        {
            var expContent = labelledSections.Where(s => s.Label == "Experience")
                                     .SelectMany(s => s.Lines)
                                     .ToList();
            if (expContent.Count > 0)
                ParseExperience(resume, expContent);
        }

        // ── 6. Parse Education ────────────────────────────────────────────────
        if (resume.Education.Count == 0)
        {
            var eduContent = labelledSections.Where(s => s.Label == "Education")
                                     .SelectMany(s => s.Lines)
                                     .ToList();
            if (eduContent.Count > 0)
                ParseEducation(resume, eduContent);
        }

        // ── 7. If no explicit Experience section, try heading-pipe heuristic ─
        if (resume.Experience.Count == 0)
            ParseExperienceFromPipeHeadings(resume, lines);
    }

    // ── Structured path (template-hint driven) ────────────────────────────────

    private static void PopulateSectionsFromStructured(
        ResumeDocument resume, IReadOnlyList<DocumentSection> sections)
    {
        foreach (var section in sections)
        {
            var type = section.SemanticType ?? SectionClassifier.ClassifyHeading(section.Heading);
            var bodyLines = section.Body.Split('\n');

            switch (type)
            {
                case "Name":
                    if (resume.Personal.FullName == null)
                        resume.Personal.FullName = section.Heading.Split('|')[0].Trim();
                    break;

                case "Summary":
                    if (resume.Personal.Summary == null && section.Body.Length > 10)
                        resume.Personal.Summary = section.Body.Trim();
                    break;

                case "Skills":
                    if (resume.Skills.Count == 0)
                        ParseSkills(resume, bodyLines.ToList());
                    break;

                case "Experience":
                    if (resume.Experience.Count == 0)
                        ParseExperience(resume, bodyLines.ToList());
                    break;

                case "Education":
                    if (resume.Education.Count == 0)
                        ParseEducation(resume, bodyLines.ToList());
                    break;
            }
        }

        // Last-resort: pipe-heading heuristic across all body text
        if (resume.Experience.Count == 0)
        {
            var allLines = sections.SelectMany(s => s.Body.Split('\n')).ToArray();
            ParseExperienceFromPipeHeadings(resume, allLines);
        }
    }

    private static void ExtractNameFromMarkdown(ResumeDocument resume, string[] lines)
    {
        if (resume.Personal.FullName != null) return;
        var firstHeading = lines.FirstOrDefault(l => l.StartsWith("# ") || l.StartsWith("## "));
        if (firstHeading == null) return;
        var raw = firstHeading.TrimStart('#').Trim();
        var namePart = raw.Split('|')[0].Trim();
        if (namePart.Length > 2 && !IsKnownSection(namePart) && !namePart.Contains('@'))
            resume.Personal.FullName = namePart.Split('.')[0].Trim();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string? ExtractSummary(string[] lines)
    {
        var sb = new System.Text.StringBuilder();
        var pastFirstHeading = false;
        var inSummary = false;

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                if (!pastFirstHeading) { pastFirstHeading = true; continue; }
                if (inSummary) break;

                var section = SectionClassifier.ClassifyHeading(line);
                if (section == "Summary") { inSummary = true; continue; }
                if (sb.Length > 0) break;
                continue;
            }

            if (!pastFirstHeading) continue;

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            sb.Append(trimmed).Append(' ');
            if (sb.Length > 800) break;
        }

        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    private record SectionBlock(string Label, List<string> Lines);

    private static List<SectionBlock> SplitIntoSections(string[] lines)
    {
        var result = new List<SectionBlock>();
        string? currentLabel = null;
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                var label = SectionClassifier.ClassifyHeading(line);
                if (label != null)
                {
                    if (currentLabel != null && currentLines.Count > 0)
                        result.Add(new SectionBlock(currentLabel, [.. currentLines]));
                    currentLabel = label;
                    currentLines.Clear();
                    continue;
                }
            }

            if (currentLabel != null)
                currentLines.Add(line);
        }

        if (currentLabel != null && currentLines.Count > 0)
            result.Add(new SectionBlock(currentLabel, currentLines));

        return result;
    }

    private static void ParseSkills(ResumeDocument resume, List<string> lines)
    {
        string? currentCategory = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Sub-headings become skill categories
            if (line.StartsWith('#'))
            {
                currentCategory = line.TrimStart('#').Trim();
                continue;
            }

            // Lines like "Server Side: C#, Python, Java"
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx < 40)
            {
                currentCategory = line[..colonIdx].Trim();
                line = line[(colonIdx + 1)..].Trim();
            }

            // Split on common skill delimiters
            var parts = line.Split([',', ';', '•', '·', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var skill = part.Trim().TrimStart('-', '*', ' ');
                // Skip noise: very short, very long, or containing full sentences
                if (skill.Length < 2 || skill.Length > 50 || skill.Contains("experience") ||
                    skill.Count(c => c == ' ') > 5) continue;

                resume.Skills.Add(new Skill { Name = skill, Category = currentCategory });
            }
        }
    }

    private static void ParseExperience(ResumeDocument resume, List<string> lines)
    {
        WorkExperience? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Pattern 1: "Company | Title | Location Dates" heading
            if (line.StartsWith('#') && line.Contains('|'))
            {
                if (current != null) resume.Experience.Add(current);
                current = ParseJobHeading(line.TrimStart('#').Trim());
                continue;
            }

            // Pattern 2: Date-range-only heading
            if (line.StartsWith('#'))
            {
                var headingRange = ResumeDateParser.ExtractFirstDateRange(line);
                if (headingRange != null)
                {
                    current ??= new WorkExperience();
                    ApplyDateRange(current, headingRange);
                    continue;
                }
            }

            // Pattern 3: "YYYY - YYYY:  Job Title, Company (Location)"
            // Common in Eastern European / Israeli CVs from Word templates
            if (!line.StartsWith('#'))
            {
                var prefixRange = ResumeDateParser.ExtractFirstDateRange(line);
                if (prefixRange != null && prefixRange.MatchStart <= 2)
                {
                    var rest = line[(prefixRange.MatchEnd + 1)..].TrimStart(':', ' ');
                    if (rest.Length > 5) // something meaningful after the date
                    {
                        if (current != null) resume.Experience.Add(current);
                        current = new WorkExperience();
                        ApplyDateRange(current, prefixRange);

                        // Try "Title, Company (Location)" — split on last comma before paren or end
                        var parenIdx = rest.IndexOf('(');
                        var baseText = parenIdx > 0 ? rest[..parenIdx].TrimEnd(',', ' ') : rest;
                        var commaIdx = baseText.LastIndexOf(',');
                        if (commaIdx > 0)
                        {
                            current.Title = baseText[..commaIdx].Trim();
                            current.Company = baseText[(commaIdx + 1)..].Trim();
                            if (parenIdx > 0)
                                current.Location = rest[(parenIdx + 1)..].TrimEnd(')').Trim();
                        }
                        else
                        {
                            current.Title = baseText.Trim();
                        }
                        continue;
                    }
                }
            }

            if (current == null) continue;

            // Inline date range that IS the full line content (date-only separator)
            var dateRange = ResumeDateParser.ExtractFirstDateRange(line);
            if (dateRange != null)
            {
                int matchLen = dateRange.MatchEnd - dateRange.MatchStart + 1;
                if (line.Length - matchLen < 20)
                {
                    ApplyDateRange(current, dateRange);
                    continue;
                }
            }

            // Achievement bullets
            if (line.Length > 20 && !line.StartsWith('#'))
                current.Achievements.Add(line.TrimStart('-', '*', '•', ' '));
        }

        if (current != null) resume.Experience.Add(current);
    }

    private static void ParseExperienceFromPipeHeadings(ResumeDocument resume, string[] lines)
    {
        WorkExperience? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('#') && line.Contains('|'))
            {
                var content = line.TrimStart('#').Trim();
                // Skip section headings like "Scott Galloway | .NET Developer | Remote"
                if (!IsJobEntry(content)) continue;

                if (current != null) resume.Experience.Add(current);
                current = ParseJobHeading(content);
                continue;
            }

            // Date headings
            if (line.StartsWith('#'))
            {
                var headingRange = ResumeDateParser.ExtractFirstDateRange(line);
                if (headingRange != null)
                {
                    current ??= new WorkExperience();
                    ApplyDateRange(current, headingRange);
                    continue;
                }
            }

            if (current == null) continue;

            var dateRange = ResumeDateParser.ExtractFirstDateRange(line);
            if (dateRange != null)
            {
                int matchLen = dateRange.MatchEnd - dateRange.MatchStart + 1;
                if (line.Length - matchLen < 20)
                {
                    ApplyDateRange(current, dateRange);
                    continue;
                }
            }

            if (line.Length > 30 && !line.StartsWith('#'))
                current.Achievements.Add(line.TrimStart('-', '*', '•', ' '));
        }

        if (current != null) resume.Experience.Add(current);
    }

    private static void ParseEducation(ResumeDocument resume, List<string> lines)
    {
        Education? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var isHeading = line.StartsWith('#');
            var content = isHeading ? line.TrimStart('#').Trim() : line;

            // Both headings and plain-text lines that look like institution entries start a new record
            if (isHeading || (current == null && (content.Contains(" | ") || content.Contains(" in "))))
            {
                if (current != null) resume.Education.Add(current);
                current = new Education();

                // "Degree in Field — Institution" or "Institution | Degree"
                if (content.Contains(" in "))
                {
                    var inIdx = content.IndexOf(" in ");
                    current.Degree = content[..inIdx].Trim();
                    var rest = content[(inIdx + 4)..];
                    var atIdx = rest.IndexOf(" — ");
                    if (atIdx > 0) { current.FieldOfStudy = rest[..atIdx].Trim(); current.Institution = rest[(atIdx + 3)..].Trim(); }
                    else current.FieldOfStudy = rest.Trim();
                }
                else if (content.Contains(" | "))
                {
                    var parts = content.Split(" | ", 2);
                    current.Institution = parts[0].Trim();
                    current.Degree = parts[1].Trim();
                }
                else current.Institution = content;

                var headingRange = ResumeDateParser.ExtractFirstDateRange(content);
                if (headingRange != null) ApplyDateRange(current, headingRange);
                continue;
            }

            if (current != null)
            {
                var dateRange = ResumeDateParser.ExtractFirstDateRange(line);
                if (dateRange != null) ApplyDateRange(current, dateRange);
                else if (line.Length > 5) current.Highlights.Add(line.TrimStart('-', '*', '•', ' '));
            }
        }

        if (current != null) resume.Education.Add(current);
    }

    private static WorkExperience ParseJobHeading(string content)
    {
        var job = new WorkExperience();
        // Pattern: "Company | Title | Location DateRange"
        var parts = content.Split('|');
        if (parts.Length >= 2)
        {
            job.Company = parts[0].Trim();
            job.Title = parts[1].Trim();
            if (parts.Length >= 3) job.Location = parts[2].Trim();
        }
        else job.Company = content;

        var dateRange = ResumeDateParser.ExtractFirstDateRange(content);
        if (dateRange != null) ApplyDateRange(job, dateRange);
        return job;
    }

    private static void ApplyDateRange(WorkExperience job, DateRangeResult range)
    {
        job.StartDate = range.Start;
        job.EndDate = range.End;
        job.IsCurrent = range.IsCurrent;
    }

    private static void ApplyDateRange(WorkExperience job, string text)
    {
        var range = ResumeDateParser.ExtractFirstDateRange(text);
        if (range == null) return;
        job.StartDate = range.Start;
        job.EndDate = range.End;
        job.IsCurrent = range.IsCurrent;
    }

    private static void ApplyDateRange(Education edu, DateRangeResult range)
    {
        edu.StartDate = range.Start;
        if (!range.IsCurrent) edu.EndDate = range.End;
    }

    private static bool IsKnownSection(string text) =>
        SectionClassifier.ClassifyHeading(text) != null;

    private static bool IsJobEntry(string content) =>
        ResumeDateParser.ContainsDate(content);
}
