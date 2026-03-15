using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Parsing;

namespace lucidRESUME.Ingestion.Parsing;

/// <summary>
/// Parses structured resume sections (Experience, Education, Skills) out of
/// the Docling-generated markdown output. Uses heuristics rather than strict
/// schema since Docling's markdown is document-layout-dependent.
/// </summary>
public static class MarkdownSectionParser
{
    // Matches date ranges like "Jan 2012 – Present", "Oct 2024 - Present", "2019 – 2022", "2020 - now"
    private static readonly Regex DateRangePattern = new(
        @"((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}|\d{4})\s*[-–]\s*((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}|\d{4}|Present|Current|now)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches date-prefixed job lines: "2020 - now:  Java Developer, Company (Location)"
    private static readonly Regex DatePrefixJobLine = new(
        @"^(\d{4})\s*[-–]\s*(\d{4}|Present|Current|now)\s*:?\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearPattern = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

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

            // Pattern 2: Date-only heading (inline date range)
            if (line.StartsWith('#') && DateRangePattern.IsMatch(line))
            {
                current ??= new WorkExperience();
                ApplyDateRange(current, line);
                continue;
            }

            // Pattern 3: "YYYY - YYYY:  Job Title, Company (Location)"
            // Common in Eastern European / Israeli CVs from Word templates
            var datePrefixMatch = DatePrefixJobLine.Match(line);
            if (datePrefixMatch.Success)
            {
                if (current != null) resume.Experience.Add(current);
                current = new WorkExperience();
                ApplyDateRange(current, line);

                var rest = datePrefixMatch.Groups[3].Value.Trim();
                // Try "Title, Company (Location)" — split on last comma before a paren or end
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

            if (current == null) continue;

            // Inline date range that IS the full line content (date-only separator)
            var dateMatch = DateRangePattern.Match(line);
            if (dateMatch.Success && line.Replace(dateMatch.Value, "").Trim().Length < 20)
            {
                ApplyDateRange(current, line);
                continue;
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
            if (line.StartsWith('#') && DateRangePattern.IsMatch(line))
            {
                current ??= new WorkExperience();
                ApplyDateRange(current, line);
                continue;
            }

            if (current == null) continue;

            var dateMatch = DateRangePattern.Match(line);
            if (dateMatch.Success && line.Replace(dateMatch.Value, "").Trim().Length < 20)
            {
                ApplyDateRange(current, line);
                continue;
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

                ApplyDateRange(current, content);
                continue;
            }

            if (current != null)
            {
                var dateMatch = DateRangePattern.Match(line);
                if (dateMatch.Success) ApplyDateRange(current, line);
                else if (line.Length > 5) current.Highlights.Add(line.TrimStart('-', '*', '•', ' '));
            }
        }

        if (current != null) resume.Education.Add(current);
    }

    private static WorkExperience ParseJobHeading(string content)
    {
        var job = new WorkExperience();
        // Pattern: "Company | Title | Location DateRange"
        // or: "Company | Title | Location\nDateRange"
        var parts = content.Split('|');
        if (parts.Length >= 2)
        {
            job.Company = parts[0].Trim();
            job.Title = parts[1].Trim();
            if (parts.Length >= 3) job.Location = parts[2].Trim();
        }
        else job.Company = content;

        ApplyDateRange(job, content);
        return job;
    }

    private static void ApplyDateRange(WorkExperience job, string text)
    {
        var m = DateRangePattern.Match(text);
        if (!m.Success) return;
        job.StartDate = ParseDate(m.Groups[1].Value);
        var endStr = m.Groups[2].Value;
        job.IsCurrent = endStr.Equals("Present", StringComparison.OrdinalIgnoreCase) ||
                        endStr.Equals("Current", StringComparison.OrdinalIgnoreCase);
        if (!job.IsCurrent) job.EndDate = ParseDate(endStr);
    }

    private static void ApplyDateRange(Education edu, string text)
    {
        var m = DateRangePattern.Match(text);
        if (!m.Success) return;
        edu.StartDate = ParseDate(m.Groups[1].Value);
        var endStr = m.Groups[2].Value;
        if (!endStr.Equals("Present", StringComparison.OrdinalIgnoreCase))
            edu.EndDate = ParseDate(endStr);
    }

    private static DateOnly? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParseExact(s.Trim(), ["MMM yyyy", "MMMM yyyy"], null,
            System.Globalization.DateTimeStyles.None, out var d)) return d;
        if (int.TryParse(s.Trim(), out var year)) return new DateOnly(year, 1, 1);
        return null;
    }

    private static bool IsKnownSection(string text) =>
        SectionClassifier.ClassifyHeading(text) != null;

    private static bool IsJobEntry(string content)
    {
        // A job entry has a company/title pattern — contains at least one year or date
        return YearPattern.IsMatch(content) || DateRangePattern.IsMatch(content);
    }
}
