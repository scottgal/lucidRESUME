using System.Text.RegularExpressions;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Fast structural/template extractor. Runs first, high confidence.
/// Looks at document structure (headings, bullet lists, explicit labels, patterns).
/// </summary>
public static partial class StructuralExtractor
{
    public static List<JdFieldCandidate> Extract(string text)
    {
        var candidates = new List<JdFieldCandidate>();
        var lines = text.Split('\n', StringSplitOptions.TrimEntries);
        var lower = text.ToLowerInvariant();

        // ── Title + Company ──────────────────────────────────────────────
        ExtractTitleCompany(candidates, lines);

        // ── Explicit labelled fields: "Company:", "Location:", etc. ───────
        ExtractLabelledFields(candidates, lines);

        // ── Skills from bulleted/numbered sections ───────────────────────
        ExtractBulletedSection(candidates, lines, "skill",
            ["required skills", "requirements", "required", "skills", "technical skills",
             "must have", "what we need", "what you'll need", "key skills",
             "what you'll bring", "qualifications", "you'll need",
             "what we're looking for", "what you bring", "experience required",
             "required qualifications", "minimum qualifications", "basic qualifications",
             "key requirements", "essential skills", "core competencies"]);
        ExtractBulletedSection(candidates, lines, "preferredskill",
            ["nice to have", "nice-to-have", "desirable", "bonus", "good to have",
             "preferred skills", "preferred qualifications", "preferred requirements",
             "bonus skills", "additional skills", "plus", "a plus",
             "ideal candidate", "preferred experience"]);

        // ── Responsibilities ─────────────────────────────────────────────
        ExtractBulletedSection(candidates, lines, "responsibility",
            ["responsibilities", "you will", "what you'll do", "what you will do",
             "key duties", "your role", "job duties", "role description",
             "day to day", "day-to-day", "in this role"]);

        // ── Benefits ─────────────────────────────────────────────────────
        ExtractBulletedSection(candidates, lines, "benefit",
            ["benefits", "perks", "what we offer", "we offer",
             "compensation", "why join", "why work"]);

        // ── Salary ───────────────────────────────────────────────────────
        ExtractSalary(candidates, text);

        // ── Remote / Hybrid ──────────────────────────────────────────────
        if (lower.Contains("fully remote") || lower.Contains("100% remote") || lower.Contains("remote: yes")
            || lower.Contains("location: remote") || lower.Contains("remote position"))
            candidates.Add(new("remote", "true", 0.95, "structural"));
        else if (RemotePatternRx().IsMatch(lower))
            candidates.Add(new("remote", "true", 0.8, "structural"));
        if (lower.Contains("hybrid") || HybridDaysRx().IsMatch(lower))
            candidates.Add(new("remote", "hybrid", 0.7, "structural"));

        // ── Years of experience ──────────────────────────────────────────
        ExtractYears(candidates, text);

        // ── Education ────────────────────────────────────────────────────
        var eduMatch = EducationRx().Match(text);
        if (eduMatch.Success)
            candidates.Add(new("education", eduMatch.Value.Trim(), 0.8, "structural"));

        // ── Seniority from title ─────────────────────────────────────────
        var titleCandidate = candidates.FirstOrDefault(c => c.FieldType == "title");
        if (titleCandidate is not null)
        {
            var titleLower = titleCandidate.Value.ToLowerInvariant();
            var seniority = titleLower switch
            {
                _ when titleLower.Contains("junior") || titleLower.Contains("entry level") || titleLower.Contains("graduate") => "Junior",
                _ when titleLower.Contains("senior") || titleLower.Contains("sr.") || titleLower.Contains("sr ") => "Senior",
                _ when titleLower.Contains("lead") || titleLower.Contains("principal") || titleLower.Contains("staff") => "Lead",
                _ when titleLower.Contains("head of") || titleLower.Contains("director") || titleLower.Contains("vp ") => "Principal",
                _ when titleLower.Contains("intern") => "Intern",
                _ => null
            };
            if (seniority is not null)
                candidates.Add(new("seniority", seniority, 0.85, "structural"));
        }

        return candidates;
    }

    private static void ExtractTitleCompany(List<JdFieldCandidate> candidates, string[] lines)
    {
        var firstLine = lines.FirstOrDefault(l => l.Length > 5 && !l.StartsWith("http"));
        if (firstLine is null) return;

        var clean = firstLine.TrimStart('#').Trim();
        foreach (var sep in new[] { " - ", " – ", " — ", " | ", " at " })
        {
            var parts = clean.Split(sep, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 2 && parts[1].Length > 2)
            {
                candidates.Add(new("title", parts[0], 0.9, "structural"));
                var secondLower = parts[1].ToLowerInvariant().Trim();
                if (secondLower is "remote" or "hybrid" or "onsite" or "on-site"
                    or "fully remote" or "100% remote" or "work from home")
                    candidates.Add(new("remote", "true", 0.9, "structural"));
                else
                    candidates.Add(new("company", parts[1], 0.85, "structural"));
                return;
            }
        }
        if (clean.Length < 80)
            candidates.Add(new("title", clean, 0.7, "structural"));
    }

    private static void ExtractLabelledFields(List<JdFieldCandidate> candidates, string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.Length < 5 || line.Length > 200) continue;
            var trimmed = line.TrimStart('#').Trim();

            // Company: X / Employer: X / Organisation: X / Hiring: X
            var companyMatch = CompanyLabelRx().Match(trimmed);
            if (companyMatch.Success)
            {
                var val = companyMatch.Groups[1].Value.Trim();
                if (val.Length > 1 && val.Length < 80)
                    candidates.Add(new("company", val, 0.90, "structural"));
                continue;
            }

            // Location: X / Based in: X / Office: X
            var locationMatch = LocationLabelRx().Match(trimmed);
            if (locationMatch.Success)
            {
                var val = locationMatch.Groups[1].Value.Trim();
                // Strip trailing work arrangement info
                val = WorkArrangementSuffixRx().Replace(val, "").Trim().TrimEnd(',', '-', '–');
                if (val.Length > 1 && val.Length < 80)
                    candidates.Add(new("location", val, 0.90, "structural"));
                continue;
            }

            // Salary: X / Compensation: X / Pay: X
            var salaryLabelMatch = SalaryLabelRx().Match(trimmed);
            if (salaryLabelMatch.Success)
            {
                var val = salaryLabelMatch.Groups[1].Value.Trim();
                // Try to extract numbers from the value
                var nums = SalaryNumbersRx().Matches(val);
                if (nums.Count >= 2)
                {
                    candidates.Add(new("salary_min", CleanNumber(nums[0].Value), 0.90, "structural"));
                    candidates.Add(new("salary_max", CleanNumber(nums[1].Value), 0.90, "structural"));
                }
                else if (nums.Count == 1)
                {
                    candidates.Add(new("salary_min", CleanNumber(nums[0].Value), 0.85, "structural"));
                    candidates.Add(new("salary_max", CleanNumber(nums[0].Value), 0.85, "structural"));
                }
                continue;
            }

            // Type: Full time / Contract / Part time
            var typeMatch = TypeLabelRx().Match(trimmed);
            if (typeMatch.Success)
            {
                var val = typeMatch.Groups[1].Value.Trim().ToLowerInvariant();
                if (val.Contains("contract") || val.Contains("freelance"))
                    candidates.Add(new("contracttype", "contract", 0.85, "structural"));
                continue;
            }
        }
    }

    private static void ExtractSalary(List<JdFieldCandidate> candidates, string text)
    {
        // Already extracted via labelled field?
        if (candidates.Any(c => c.FieldType == "salary_min")) return;

        // Pattern 1: £/$€NN,NNN - £/$€NN,NNN (standard)
        var m = CurrencySalaryRx().Match(text);
        if (m.Success)
        {
            candidates.Add(new("salary_min", CleanNumber(m.Groups[1].Value), 0.95, "structural"));
            candidates.Add(new("salary_max", CleanNumber(m.Groups[2].Value), 0.95, "structural"));
            return;
        }

        // Pattern 2: NNNk - NNNk (shorthand)
        var kMatch = KSalaryRx().Match(text);
        if (kMatch.Success)
        {
            candidates.Add(new("salary_min", (int.Parse(kMatch.Groups[1].Value) * 1000).ToString(), 0.85, "structural"));
            candidates.Add(new("salary_max", (int.Parse(kMatch.Groups[2].Value) * 1000).ToString(), 0.85, "structural"));
            return;
        }

        // Pattern 3: RM N,NNN – RM N,NNN per month (Malaysian Ringgit)
        var rmMatch = RmSalaryRx().Match(text);
        if (rmMatch.Success)
        {
            candidates.Add(new("salary_min", CleanNumber(rmMatch.Groups[1].Value), 0.90, "structural"));
            candidates.Add(new("salary_max", CleanNumber(rmMatch.Groups[2].Value), 0.90, "structural"));
            candidates.Add(new("salary_currency", "MYR", 0.90, "structural"));
            candidates.Add(new("salary_period", "monthly", 0.85, "structural"));
            return;
        }

        // Pattern 4: $NNN,NNN.NN (single value — Adzuna format)
        var singleMatch = SingleSalaryRx().Match(text);
        if (singleMatch.Success)
        {
            var val = CleanNumber(singleMatch.Groups[1].Value);
            candidates.Add(new("salary_min", val, 0.80, "structural"));
            candidates.Add(new("salary_max", val, 0.80, "structural"));
        }
    }

    private static void ExtractYears(List<JdFieldCandidate> candidates, string text)
    {
        // "N+ years of experience" / "minimum N years" / "at least N years" / "N-M years"
        var patterns = new[]
        {
            YearsRx1(), // N+ years of experience
            YearsRx2(), // minimum/at least N years
            YearsRx3(), // N-M years (take min)
        };

        foreach (var rx in patterns)
        {
            var m = rx.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var yrs) && yrs is > 0 and < 50)
            {
                candidates.Add(new("yearsexp", yrs.ToString(), 0.9, "structural"));
                return;
            }
        }
    }

    private static void ExtractBulletedSection(List<JdFieldCandidate> candidates,
        string[] lines, string fieldType, string[] sectionKeywords)
    {
        bool inSection = false;
        int blanks = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineLower = line.ToLowerInvariant().TrimStart('#').Trim().TrimEnd(':');

            if (line.Length < 60 && sectionKeywords.Any(k => lineLower.Contains(k)))
            {
                inSection = true;
                blanks = 0;

                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0 && colonIdx < line.Length - 5)
                {
                    var after = line[(colonIdx + 1)..].Trim();
                    if (after.Length > 3)
                        AddItemsFromLine(candidates, after, fieldType, 0.85);
                }
                continue;
            }

            if (!inSection) continue;

            if (string.IsNullOrWhiteSpace(line)) { blanks++; if (blanks > 1) inSection = false; continue; }
            if (line.StartsWith('#') || (line.Length < 50 && line.EndsWith(':')))
            {
                var nextLower = line.ToLowerInvariant().TrimStart('#').Trim().TrimEnd(':');
                if (!sectionKeywords.Any(k => nextLower.Contains(k)))
                { inSection = false; continue; }
            }
            blanks = 0;

            // Bullet, dash, number prefix, or plain line
            var trimmed = line.TrimStart('-', '•', '*', '·', ' ');
            // Strip numbered prefix: "1. ", "1) "
            if (trimmed.Length > 2 && char.IsDigit(trimmed[0]))
            {
                var numEnd = trimmed.IndexOfAny(['.', ')']);
                if (numEnd > 0 && numEnd < 4)
                    trimmed = trimmed[(numEnd + 1)..].Trim();
            }

            if (trimmed.Length > 2)
                AddItemsFromLine(candidates, trimmed, fieldType, 0.8);
        }
    }

    private static void AddItemsFromLine(List<JdFieldCandidate> candidates,
        string line, string fieldType, double baseConfidence)
    {
        // Clean markdown bold/italic artifacts
        var cleaned = line.Replace("**", "").Replace("__", "").Replace("*", "").Trim();

        var items = SplitRespectingParens(cleaned);
        if (items.Count > 1)
        {
            foreach (var item in items)
            {
                var clean = item.Trim().TrimStart('-', '•', ' ');
                if (clean.Length > 2 && clean.Length < 120)
                    candidates.Add(new(fieldType, clean, baseConfidence, "structural"));
            }
        }
        else if (cleaned.Length > 2 && cleaned.Length < 200)
        {
            candidates.Add(new(fieldType, cleaned, baseConfidence, "structural"));
        }
    }

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

    private static string CleanNumber(string s) =>
        s.Replace(",", "").Replace(" ", "").TrimEnd('.');

    // ── Compiled Regex patterns ──────────────────────────────────────────

    [GeneratedRegex(@"(?:location|remote)\s*[:(].*remote", RegexOptions.IgnoreCase)]
    private static partial Regex RemotePatternRx();

    [GeneratedRegex(@"\d\s*days?\s*(?:per\s*week|\/\s*week|in\s*office|on[\s-]?site)", RegexOptions.IgnoreCase)]
    private static partial Regex HybridDaysRx();

    [GeneratedRegex(@"^(?:company|employer|organisation|organization|hiring)\s*[:\-–]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CompanyLabelRx();

    [GeneratedRegex(@"^(?:location|based in|office|city|region)\s*[:\-–]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LocationLabelRx();

    [GeneratedRegex(@"(?:\(?\s*(?:remote|hybrid|on[\s-]?site|work from home|fully remote|wfh)\s*\)?)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkArrangementSuffixRx();

    [GeneratedRegex(@"^(?:salary|compensation|pay|pay range|salary range)\s*[:\-–]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SalaryLabelRx();

    [GeneratedRegex(@"[\d,]+\.?\d*")]
    private static partial Regex SalaryNumbersRx();

    [GeneratedRegex(@"^(?:type|employment type|contract|job type)\s*[:\-–]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TypeLabelRx();

    [GeneratedRegex(@"[£$€]([\d,]+)\s*[-–]\s*[£$€]?([\d,]+)")]
    private static partial Regex CurrencySalaryRx();

    [GeneratedRegex(@"(\d+)k\s*[-–]\s*(\d+)k", RegexOptions.IgnoreCase)]
    private static partial Regex KSalaryRx();

    [GeneratedRegex(@"RM\s*([\d,]+)\s*[-–]\s*RM\s*([\d,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RmSalaryRx();

    [GeneratedRegex(@"(?:salary|compensation|pay)[:\s]*\$?([\d,]+\.?\d*)", RegexOptions.IgnoreCase)]
    private static partial Regex SingleSalaryRx();

    [GeneratedRegex(@"(\d+)\+?\s*years?\s+(?:of\s+)?(?:experience|exp)", RegexOptions.IgnoreCase)]
    private static partial Regex YearsRx1();

    [GeneratedRegex(@"(?:minimum|at least|min)\s+(\d+)\s*\+?\s*years?", RegexOptions.IgnoreCase)]
    private static partial Regex YearsRx2();

    [GeneratedRegex(@"(\d+)\s*[-–]\s*\d+\s*years?\s+(?:of\s+)?(?:experience|exp)", RegexOptions.IgnoreCase)]
    private static partial Regex YearsRx3();

    [GeneratedRegex(@"(?:bachelor|master|phd|degree|b\.?s\.?c?|m\.?s\.?c?|mba)\s+(?:in\s+)?[\w\s,/]+", RegexOptions.IgnoreCase)]
    private static partial Regex EducationRx();
}
