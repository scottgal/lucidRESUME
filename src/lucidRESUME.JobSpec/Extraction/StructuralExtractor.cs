using System.Text.RegularExpressions;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Fast structural/template extractor. Runs first, high confidence.
/// Looks at document structure (headings, bullet lists, first line) not content semantics.
/// </summary>
public static class StructuralExtractor
{
    public static List<JdFieldCandidate> Extract(string text)
    {
        var candidates = new List<JdFieldCandidate>();
        var lines = text.Split('\n', StringSplitOptions.TrimEntries);

        // Title from first non-empty line (structural: first line IS the title)
        var firstLine = lines.FirstOrDefault(l => l.Length > 5 && !l.StartsWith("http"));
        if (firstLine is not null)
        {
            var clean = firstLine.TrimStart('#').Trim();
            // Split on common separators
            foreach (var sep in new[] { " - ", " – ", " - ", " | ", " at " })
            {
                var parts = clean.Split(sep, 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Length > 2 && parts[1].Length > 2)
                {
                    candidates.Add(new("title", parts[0], 0.9, "structural"));
                    // Don't treat work arrangements as company names
                    var secondLower = parts[1].ToLowerInvariant().Trim();
                    if (secondLower is "remote" or "hybrid" or "onsite" or "on-site"
                        or "fully remote" or "100% remote" or "work from home")
                        candidates.Add(new("remote", "true", 0.9, "structural"));
                    else
                        candidates.Add(new("company", parts[1], 0.85, "structural"));
                    break;
                }
            }
            if (!candidates.Any(c => c.FieldType == "title") && clean.Length < 80)
                candidates.Add(new("title", clean, 0.7, "structural"));
        }

        // Skills from bulleted lists under section headings
        ExtractBulletedSection(candidates, lines, "skill",
            ["required skills", "requirements", "required", "skills", "technical skills",
             "must have", "what we need", "what you'll need", "key skills",
             "what you'll bring", "qualifications", "you'll need"]);
        ExtractBulletedSection(candidates, lines, "preferredskill",
            ["nice to have", "nice-to-have", "desirable", "bonus", "good to have",
             "preferred skills", "preferred qualifications"]);

        // Salary from currency pattern (structural: £/$/€ followed by number)
        var salaryMatch = Regex.Match(text, @"[£$€]([\d,]+)\s*[-–]\s*[£$€]?([\d,]+)");
        if (salaryMatch.Success)
        {
            candidates.Add(new("salary_min", salaryMatch.Groups[1].Value.Replace(",", ""), 0.95, "structural"));
            candidates.Add(new("salary_max", salaryMatch.Groups[2].Value.Replace(",", ""), 0.95, "structural"));
        }

        // Remote from explicit keywords
        var lower = text.ToLowerInvariant();
        if (lower.Contains("fully remote") || lower.Contains("100% remote") || lower.Contains("remote: yes"))
            candidates.Add(new("remote", "true", 0.95, "structural"));
        else if (Regex.IsMatch(lower, @"(?:location|remote)\s*[:(].*remote"))
            candidates.Add(new("remote", "true", 0.8, "structural"));
        if (lower.Contains("hybrid"))
            candidates.Add(new("remote", "hybrid", 0.7, "structural"));

        // Years from "X+ years" pattern
        var yearsMatch = Regex.Match(text, @"(\d+)\+?\s*years?\s+(?:of\s+)?experience", RegexOptions.IgnoreCase);
        if (yearsMatch.Success)
            candidates.Add(new("yearsexp", yearsMatch.Groups[1].Value, 0.9, "structural"));

        return candidates;
    }

    private static void ExtractBulletedSection(List<JdFieldCandidate> candidates,
        string[] lines, string fieldType, string[] sectionKeywords)
    {
        bool inSection = false;
        int blanks = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lower = line.ToLowerInvariant().TrimStart('#').Trim().TrimEnd(':');

            // Check if this line is a section header matching our keywords
            if (line.Length < 60 && sectionKeywords.Any(k => lower.Contains(k)))
            {
                inSection = true;
                blanks = 0;

                // Check for inline items on the same line after a colon
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0 && colonIdx < line.Length - 5)
                {
                    var after = line[(colonIdx + 1)..].Trim();
                    if (after.Length > 3)
                        AddSkillsFromLine(candidates, after, fieldType, 0.85);
                }
                continue;
            }

            if (!inSection) continue;

            // End of section: blank lines or new heading
            if (string.IsNullOrWhiteSpace(line)) { blanks++; if (blanks > 1) { inSection = false; } continue; }
            if (line.StartsWith('#') || (line.Length < 50 && line.EndsWith(':')))
            {
                // Check if this is a new known section - if so, stop
                var nextLower = line.ToLowerInvariant().TrimStart('#').Trim().TrimEnd(':');
                if (!sectionKeywords.Any(k => nextLower.Contains(k)))
                { inSection = false; continue; }
            }
            blanks = 0;

            // Bullet line: "- Item" or "• Item"
            var trimmed = line.TrimStart('-', '•', '*', '·', ' ');
            if (trimmed.Length > 2)
                AddSkillsFromLine(candidates, trimmed, fieldType, 0.8);
        }
    }

    private static void AddSkillsFromLine(List<JdFieldCandidate> candidates,
        string line, string fieldType, double baseConfidence)
    {
        // Split on commas/semicolons but NOT inside parentheses
        // "Deep expertise in C#, .NET 8, ASP.NET Core" → 3 items
        // "Azure (AKS, Functions, Service Bus)" → 1 item (parens preserved)
        var items = SplitRespectingParens(line);

        if (items.Count > 1)
        {
            foreach (var item in items)
            {
                var clean = item.Trim().TrimStart('-', '•', ' ');
                if (clean.Length > 2 && clean.Length < 100)
                    candidates.Add(new(fieldType, clean, baseConfidence, "structural"));
            }
        }
        else if (line.Length > 2 && line.Length < 120)
        {
            candidates.Add(new(fieldType, line.Trim(), baseConfidence, "structural"));
        }
    }

    /// <summary>Split on commas/semicolons but skip separators inside parentheses.</summary>
    private static List<string> SplitRespectingParens(string text)
    {
        var result = new List<string>();
        int depth = 0, start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(' or '[': depth++; break;
                case ')' or ']': depth = Math.Max(0, depth - 1); break;
                case ',' or ';' when depth == 0:
                    var seg = text[start..i].Trim();
                    if (seg.Length > 0) result.Add(seg);
                    start = i + 1;
                    break;
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }
}