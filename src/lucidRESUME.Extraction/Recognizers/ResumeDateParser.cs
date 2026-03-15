using Microsoft.Recognizers.Text.DateTime;

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

        List<Microsoft.Recognizers.Text.ModelResult> results;
        try { results = DateTimeRecognizer.RecognizeDateTime(text, Culture); }
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

    private static bool IsOpenEndedInText(string text, int start, int end)
    {
        var safeEnd = Math.Min(end, text.Length - 1);
        if (start > safeEnd) return false;
        var slice = text[start..(safeEnd + 1)];
        return slice.Contains("now", StringComparison.OrdinalIgnoreCase)
            || slice.Contains("present", StringComparison.OrdinalIgnoreCase)
            || slice.Contains("current", StringComparison.OrdinalIgnoreCase)
            || slice.Contains("to date", StringComparison.OrdinalIgnoreCase)
            || slice.Contains("till now", StringComparison.OrdinalIgnoreCase);
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
