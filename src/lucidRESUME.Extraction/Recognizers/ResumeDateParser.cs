using Microsoft.Recognizers.Text.DateTime;
using System.Globalization;
using System.Text.RegularExpressions;

namespace lucidRESUME.Extraction.Recognizers;

/// <summary>
/// Rule-based date range extractor using Microsoft.Recognizers.Text.DateTime.
/// Handles all common resume date formats without brittle regex:
///   "Jan 2020 – Present", "2019–2022", "2020 - now", "Oct 2018 to date",
///   "January 2016 – December 2019", "2015–present", etc.
/// </summary>
public static class ResumeDateParser
{
    private const string Culture = "en-us";

    /// <summary>
    /// Extracts the first date range found in <paramref name="text"/>.
    /// Returns null when no date range is recognizable.
    /// </summary>
    public static DateRangeResult? ExtractFirstDateRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Recognizers.Text treats a UK numeric date followed by "Present" as one
        // date rather than an open range. Handle that common CV heading explicitly
        // and preserve exact source offsets for the structural parser.
        var numericOpen = Regex.Match(text,
            @"(?<date>\b\d{1,2}/\d{1,2}/\d{4})\s*(?:[-–—]|to)\s*(?:present|current|now|to date)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (numericOpen.Success && DateOnly.TryParseExact(numericOpen.Groups["date"].Value,
                ["d/M/yyyy", "dd/MM/yyyy", "M/d/yyyy", "MM/dd/yyyy"],
                CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var numericStart))
            return new DateRangeResult(numericStart, null, true,
                numericOpen.Index, numericOpen.Index + numericOpen.Length - 1);

        // Labels such as "Start Date:" and "End Date:" confuse the generic recognizer
        // ("End Date" has been observed resolving to October). Blank the labels while
        // preserving their length so the returned source offsets remain valid.
        var recognizerText = Regex.Replace(text, @"\b(?:start|end)\s+date\s*:\s*",
            match => new string(' ', match.Length), RegexOptions.IgnoreCase);

        List<Microsoft.Recognizers.Text.ModelResult> results;
        try { results = DateTimeRecognizer.RecognizeDateTime(recognizerText, Culture); }
        catch { return null; }

        foreach (var result in results)
        {
            if (result.Resolution?.TryGetValue("values", out var raw) != true) continue;
            if (raw is not IList<Dictionary<string, string>> values) continue;

            foreach (var v in values)
            {
                if (!v.TryGetValue("type", out var type)) continue;
                if (type is not ("daterange" or "datetimerange")) continue;

                v.TryGetValue("start", out var startStr);
                v.TryGetValue("end", out var endStr);
                v.TryGetValue("timex", out var timex);

                // Detect open-ended / present references
                var isCurrent =
                    (timex != null && timex.Contains("PRESENT_REF", StringComparison.OrdinalIgnoreCase)) ||
                    string.IsNullOrEmpty(endStr) ||
                    IsOpenEndedInText(text, result.Start, result.End);

                return new DateRangeResult(
                    ParseIsoDate(startStr),
                    isCurrent ? null : ParseIsoDate(endStr),
                    isCurrent,
                    result.Start,
                    result.End);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if the text contains any recognizable date expression.
    /// Used to determine whether a heading looks like a job or education entry.
    /// </summary>
    public static bool ContainsDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { return DateTimeRecognizer.RecognizeDateTime(text, Culture).Count > 0; }
        catch { return false; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsOpenEndedInText(string text, int matchStart, int matchEnd)
    {
        // Scan from the date match start to end of text.
        // Recognizers.Text en-us does NOT classify "Present" / "present" as PRESENT_REF,
        // so we must detect it ourselves. Searching from matchStart avoids false-positives
        // from "present" / "currently" that appear BEFORE the date range in a sentence.
        _ = matchEnd; // matchEnd reserved for future narrower checks
        if (matchStart < 0 || matchStart >= text.Length) return false;
        var fromDate = text[matchStart..];
        return fromDate.Contains("present", StringComparison.OrdinalIgnoreCase)
            || fromDate.Contains("current", StringComparison.OrdinalIgnoreCase)
            || fromDate.Contains("now", StringComparison.OrdinalIgnoreCase)
            || fromDate.Contains("to date", StringComparison.OrdinalIgnoreCase)
            || fromDate.Contains("till now", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? ParseIsoDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        var s = iso.AsSpan().Trim();
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var d)) return d;
        if (DateOnly.TryParseExact(s, "yyyy-MM", null,
            System.Globalization.DateTimeStyles.None, out d)) return d;
        if (DateOnly.TryParseExact(s, "yyyy", null,
            System.Globalization.DateTimeStyles.None, out d)) return d;
        return null;
    }
}

/// <summary>Parsed date range extracted from resume text.</summary>
/// <param name="Start">Start date of the range (null if not resolved).</param>
/// <param name="End">End date of the range (null when <paramref name="IsCurrent"/> is true).</param>
/// <param name="IsCurrent">True when the end of the range is open / "present".</param>
/// <param name="MatchStart">Zero-based index of the first character of the match in source text.</param>
/// <param name="MatchEnd">Zero-based index of the last character of the match in source text (inclusive).</param>
public sealed record DateRangeResult(
    DateOnly? Start,
    DateOnly? End,
    bool IsCurrent,
    int MatchStart,
    int MatchEnd);
