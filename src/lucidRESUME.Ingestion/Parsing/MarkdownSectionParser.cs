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
            // Fallback: if skills still empty, scan pre-heading area for "Hard Skills:" etc.
            if (resume.Skills.Count == 0)
                ExtractSkillsFromPreHeader(resume, markdown.Split('\n'));
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

        // ── 8. Last-resort: scan ALL lines for inline "Professional Experience:" labels ──
        if (resume.Experience.Count == 0)
            ExtractExperienceFromInlineLabel(resume, lines);
    }

    // ── Structured path (template-hint driven) ────────────────────────────────

    private static void PopulateSectionsFromStructured(
        ResumeDocument resume, IReadOnlyList<DocumentSection> sections)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
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
                    {
                        var lines = string.IsNullOrWhiteSpace(section.Body)
                            ? CollectUnclassifiedFollowingSections(sections, i)
                            : bodyLines.ToList();
                        ParseSkills(resume, lines);
                    }
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

        // Inline-label fallback across all sections (e.g. "Professional Experience:" outside heading)
        if (resume.Experience.Count == 0)
        {
            var allLines = sections.SelectMany(s => new[] { s.Heading }.Concat(s.Body.Split('\n'))).ToArray();
            ExtractExperienceFromInlineLabel(resume, allLines);
        }
    }

    /// <summary>
    /// When a known section (e.g. Skills) has an empty body — common in two-column
    /// table CVs where the adjacent column heading immediately follows — collect
    /// content from the next sections that are unclassified (no semantic type and
    /// no known section classifier match), treating their headings as content lines.
    /// Stops at the first section that has a known semantic type.
    /// </summary>
    private static List<string> CollectUnclassifiedFollowingSections(
        IReadOnlyList<DocumentSection> sections, int fromIndex)
    {
        var lines = new List<string>();
        for (int j = fromIndex + 1; j < sections.Count; j++)
        {
            var s = sections[j];
            var t = s.SemanticType ?? SectionClassifier.ClassifyHeading(s.Heading);
            if (t != null) break; // Stop at the next classified section

            // Include heading as a content line (it may itself be a skill name like "HTML5")
            if (!string.IsNullOrWhiteSpace(s.Heading))
                lines.Add(s.Heading);
            if (!string.IsNullOrWhiteSpace(s.Body))
                lines.AddRange(s.Body.Split('\n'));
        }
        return lines;
    }

    // Labels that signal a skill list even outside a dedicated Skills section.
    private static bool IsSkillLabel(string label) =>
        label.Contains("skill") || label == "technologies" || label == "stack"
        || label == "technical" || label.Contains("competenc") || label.Contains("expertise");

    /// <summary>
    /// Fallback skill extraction: scans the entire markdown for "Hard Skills:" /
    /// "Technical Skills:" / "Technical Skills Set" style labels, extracting the
    /// comma-separated items that follow on the same or next non-empty line.
    /// Runs after the structured path when skills are still empty.
    /// </summary>
    private static void ExtractSkillsFromPreHeader(ResumeDocument resume, string[] lines)
    {
        string? pendingCategory = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) { pendingCategory = null; continue; }

            // Skip markdown headings but don't stop scanning
            if (line.StartsWith('#')) { pendingCategory = null; continue; }

            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx < 50)
            {
                var label = line[..colonIdx].Trim().ToLowerInvariant();
                if (IsSkillLabel(label))
                {
                    var rest = line[(colonIdx + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(rest))
                        ParseSkills(resume, [rest]);
                    else
                        pendingCategory = label; // Items follow on next line(s)
                    continue;
                }
            }

            if (pendingCategory != null)
            {
                // Collect lines that are category: item pairs (e.g. "API/frameworks: Alamofire, SwiftyJSON")
                ParseSkills(resume, [line]);
                // Keep pending until a blank line or new labeled section
                if (!line.Contains(':'))
                    pendingCategory = null;
            }
        }
    }

    private static bool IsExperienceLabel(string label) =>
        label.Contains("experience") || label == "employment" || label == "career"
        || label.Contains("work history") || label.Contains("professional history");

    /// <summary>
    /// Scans all lines for inline experience labels like "Professional Experience:" /
    /// "Work experience:" that appear as plain text (not ### headings). Extracts
    /// date-prefixed job entries that follow.
    /// </summary>
    private static void ExtractExperienceFromInlineLabel(ResumeDocument resume, string[] lines)
    {
        bool inBlock = false;
        var blockLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // A known markdown section heading ends the experience block
            if (line.StartsWith('#'))
            {
                var sectionType = SectionClassifier.ClassifyHeading(line);
                if (sectionType != null && sectionType != "Experience")
                    inBlock = false;
                continue;
            }

            // Detect inline label: "Professional Experience:" / "Work experience:"
            if (!inBlock)
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0 && colonIdx < 50)
                {
                    var label = line[..colonIdx].Trim().ToLowerInvariant();
                    if (IsExperienceLabel(label))
                    {
                        inBlock = true;
                        // Remainder of this line (after colon) may be a job entry too
                        var rest = line[(colonIdx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(rest))
                            blockLines.Add(rest);
                    }
                }
                continue;
            }

            blockLines.Add(line);
        }

        if (blockLines.Count > 0)
            ParseExperience(resume, blockLines);
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
            //             "2021-present:  Frontend developer (remote)"      ← compact, no spaces
            // Common in Eastern European / Israeli CVs from Word templates
            if (!line.StartsWith('#'))
            {
                var prefixRange = ResumeDateParser.ExtractFirstDateRange(line);
                if (prefixRange != null && prefixRange.MatchStart <= 2)
                {
                    // Strip leading separator chars; handle compact "-present" / "-now" that the
                    // recognizer leaves outside the match span when there are no spaces around the dash.
                    var rest = StripDateTail(line[(prefixRange.MatchEnd + 1)..]);
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

        // Pattern 5: flat blob (all jobs in one line) — split on inline date ranges
        if (resume.Experience.Count == 0)
        {
            var blob = string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));
            if (blob.Length > 100)
                ParseFlatExperienceBlob(resume, blob);
        }
    }

    /// <summary>
    /// Handles the "flat paragraph" resume format where the entire experience block
    /// is a single line: Title &lt;spaces&gt; Date Company Description Title &lt;spaces&gt; Date …
    ///
    /// Strategy: find each date-range occurrence; everything between the end of the
    /// previous entry and the start of the date range is the job title; the first
    /// 1–4 Title-Case words immediately after the date range are the company name.
    /// </summary>
    /// <summary>
    /// Handles the "flat paragraph" resume format where the entire experience block
    /// is a single blob: Title &lt;many-spaces&gt; Date Company Description Title &lt;many-spaces&gt; Date …
    ///
    /// Key insight: job templates separate the title from the date with a run of
    /// 10+ spaces (visual alignment).  We use this to scan BACKWARDS from each
    /// date-range position to find where the title begins, cleanly separating it
    /// from the previous entry's description.
    /// </summary>
    private static void ParseFlatExperienceBlob(ResumeDocument resume, string blob)
    {
        var allDates = Microsoft.Recognizers.Text.DateTime.DateTimeRecognizer
            .RecognizeDateTime(blob, "en-us");

        var datePositions = allDates
            .Where(d =>
            {
                if (d.Resolution?.TryGetValue("values", out var v) == true
                    && v is IList<System.Collections.Generic.Dictionary<string, string>> list)
                {
                    var first = list.FirstOrDefault();
                    return first?.TryGetValue("type", out var t) == true
                        && (t is "daterange" or "datetimerange" or "date");
                }
                return false;
            })
            .Select(d => (Start: d.Start, End: d.End))
            .OrderBy(p => p.Start)
            .ToList();

        if (datePositions.Count == 0) return;

        for (var i = 0; i < datePositions.Count; i++)
        {
            var (dStart, dEnd) = datePositions[i];

            // Scan BACKWARDS from the date to find the title separator:
            // the last run of 10+ consecutive spaces before the date is the
            // padding between title and date in these aligned templates.
            var title = ExtractTitleBeforeDate(blob, dStart);
            if (string.IsNullOrWhiteSpace(title)) continue;

            var dateSnippet = blob[dStart..Math.Min(dEnd + 60, blob.Length)];
            var dateRange = ResumeDateParser.ExtractFirstDateRange(dateSnippet);

            var job = new WorkExperience { Title = title };
            if (dateRange != null) ApplyDateRange(job, dateRange);

            // Company: first 1-4 Title-Case words immediately after the FULL date range
            // dateRange.MatchEnd is relative to dateSnippet (starting at dStart)
            var fullDateEnd = dateRange != null ? dStart + dateRange.MatchEnd : dEnd;
            var afterDate = blob[Math.Min(fullDateEnd + 1, blob.Length - 1)..].TrimStart();
            job.Company = ExtractCompanyName(afterDate);

            resume.Experience.Add(job);
        }
    }

    /// <summary>
    /// Scans LEFT from the date's start position, collecting space-separated tokens
    /// until a sentence-ending word (ends with '.', '!' or '?') is encountered or
    /// the start of the string is reached.  Returns the collected tokens as the
    /// job title.
    ///
    /// Background: flat-layout CVs pad the title from the date with 20–50 spaces
    /// (visual column alignment).  The end of the PREVIOUS entry is always marked
    /// by sentence-terminating punctuation ("GIT, TFS." / "Trello." etc.), so
    /// stopping at the first such word cleanly separates entries.
    /// </summary>
    private static string ExtractTitleBeforeDate(string blob, int dateStart)
    {
        // titleEnd = position right before the alignment spaces before the date
        var titleEnd = dateStart;
        while (titleEnd > 0 && blob[titleEnd - 1] == ' ') titleEnd--;

        if (titleEnd == 0) return string.Empty;

        var words = new List<string>();
        var i = titleEnd - 1;

        while (i >= 0 && words.Count < 10)
        {
            // Skip word-separating spaces
            while (i >= 0 && blob[i] == ' ') i--;
            if (i < 0) break;
            if (blob[i] == '\n') break;

            // Collect one token (going right-to-left)
            var wordEnd = i;
            while (i >= 0 && blob[i] != ' ' && blob[i] != '\n') i--;
            var word = blob[(i + 1)..(wordEnd + 1)];

            // A word ending with '.' / '!' / '?' terminates the previous sentence
            if (word.Length > 0 && (word[^1] == '.' || word[^1] == '!' || word[^1] == '?'))
                break;

            words.Add(word);
        }

        words.Reverse();
        return string.Join(" ", words).Trim(' ', '-', '–', '—', '|');
    }

    private static string? ExtractCompanyName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Strip any date tail the recognizer left unmatched:
        // "– Present", "- current", "– now", etc.
        var t = text.TrimStart();
        if (t.Length > 0 && (t[0] == '-' || t[0] == '–' || t[0] == '—'))
        {
            t = t.TrimStart('-', '–', '—', ' ');
            ReadOnlySpan<string> presentWords = ["present", "current", "now", "to date", "till now"];
            foreach (var word in presentWords)
                if (t.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                { t = t[word.Length..].TrimStart(); break; }
        }

        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var company = new System.Text.StringBuilder();
        foreach (var w in words.Take(4))
        {
            // Stop at any lowercase word
            if (!char.IsUpper(w[0])) break;
            // Stop if we already have a word and this looks like a sentence-starting verb/noun
            // e.g., "Performed", "Developed", "Promotion", "Responsibilities", "Part-time"
            if (company.Length > 0 && (w.Length >= 6 &&
                (w.EndsWith("ed", StringComparison.OrdinalIgnoreCase) ||
                 w.EndsWith("ing", StringComparison.OrdinalIgnoreCase) ||
                 w.EndsWith("tion", StringComparison.OrdinalIgnoreCase) ||
                 w.EndsWith("ities", StringComparison.OrdinalIgnoreCase))
                || w.Contains('-')))  // hyphenated descriptor like "Part-time", "Full-time"
                break;
            if (company.Length > 0) company.Append(' ');
            company.Append(w);
        }
        var result = company.ToString().TrimEnd(',', '.', ';');
        return result.Length > 1 ? result : null;
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

    // Words that appear as contact-app labels or short noise in table-layout CVs —
    // skip these when starting an education entry in flat mode.
    private static readonly HashSet<string> EduNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "telegram", "whatsapp", "viber", "skype", "cell", "mobile", "phone",
        "email", "linkedin", "github", "twitter", "facebook", "instagram"
    };

    private static void ParseEducation(ResumeDocument resume, List<string> lines)
    {
        Education? current = null;
        // Buffer institution lines that arrive before the date range
        var institutionBuffer = new List<string>();

        void FlushBuffer()
        {
            if (institutionBuffer.Count == 0) return;
            if (current == null) current = new Education();
            if (current.Institution == null)
                current.Institution = string.Join(", ", institutionBuffer);
            institutionBuffer.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var isHeading = line.StartsWith('#');
            var content = isHeading ? line.TrimStart('#').Trim() : line;

            // Headings and structured patterns (" | ", " in ") start a new record
            if (isHeading || content.Contains(" | ") || content.Contains(" in "))
            {
                FlushBuffer();
                if (current != null) resume.Education.Add(current);
                current = new Education();

                // Strip a leading date prefix ("2000 - 2005: master's degree in ...")
                var headRange = ResumeDateParser.ExtractFirstDateRange(content);
                bool dateAlreadyApplied = false;
                if (headRange != null && headRange.MatchStart <= 2)
                {
                    ApplyDateRange(current, headRange);
                    content = content[(headRange.MatchEnd + 1)..].TrimStart(':', ' ');
                    dateAlreadyApplied = true;
                }

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

                if (!dateAlreadyApplied)
                {
                    var headingRange = ResumeDateParser.ExtractFirstDateRange(content);
                    if (headingRange != null) ApplyDateRange(current, headingRange);
                }
                continue;
            }

            // Date range line
            var dateRange = ResumeDateParser.ExtractFirstDateRange(line);
            if (dateRange != null)
            {
                // Commit buffered institution lines first, then apply dates
                FlushBuffer();
                if (current == null) current = new Education();
                ApplyDateRange(current, dateRange);
                continue;
            }

            // Plain content line
            if (current == null)
            {
                // Flat mode: buffer institution names that precede the date line.
                // Skip obvious contact-app noise (Telegram, WhatsApp, Cell, etc.).
                if (line.Length >= 4 && !EduNoiseWords.Contains(line.Split(' ')[0]))
                    institutionBuffer.Add(content);
            }
            else
            {
                // After a date: treat as degree/field if not yet set, otherwise highlight.
                if (current.Degree == null && line.Length > 4)
                    current.Degree = content;
                else if (line.Length > 5)
                    current.Highlights.Add(line.TrimStart('-', '*', '•', ' '));
            }
        }

        FlushBuffer();
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

    /// <summary>
    /// Strips the tail of a date-range match that the recognizer left unmatched.
    /// Handles compact formats like "-present:", "-now:", "-current:" and then
    /// trims any remaining colon/space separators.
    /// </summary>
    private static string StripDateTail(string tail)
    {
        // Trim dash/en-dash/em-dash prefix  (e.g. "-present:", "–now:")
        var s = tail.TrimStart('-', '–', '—', ' ');

        // If a present-reference keyword starts here, skip it and the following separator
        ReadOnlySpan<string> presentWords = ["present", "now", "current", "to date", "till now"];
        foreach (var word in presentWords)
        {
            if (s.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                s = s[word.Length..];
                break;
            }
        }

        return s.TrimStart(':', ' ');
    }
}
