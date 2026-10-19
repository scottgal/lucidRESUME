using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Evidence;

/// <summary>Validates extraction suggestions against candidate assertions. Results remain subject to ingestion review.</summary>
public static class SourceBoundSkillRecovery
{
    public static IReadOnlyList<string> Recover(string source, string? proposed)
    {
        var quotedJob = Regex.Match(source, @"\b(?:target job requirements|vacancy requirements|target job description)\s*:", RegexOptions.IgnoreCase);
        if (quotedJob.Success) source = source[..quotedJob.Index];
        var candidate = string.Join('\n', Regex.Split(source, @"(?<=[.!?])\s+(?=[A-Z])|[;\r\n]+")
            .Where(part => !Regex.IsMatch(part, @"\b(?:training|learning|study)\b[^.!?]*\bplanned\b|\b(?:plan(?:ned)?|hope|want|intend) to (?:learn|study|train)\b", RegexOptions.IgnoreCase)));
        return (proposed ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.TrimStart('-', '*', '•', ' ').Trim())
            .Where(term => term.Length is >= 2 and <= 50 && !term.Equals("NONE", StringComparison.OrdinalIgnoreCase) &&
                !term.Contains("experience", StringComparison.OrdinalIgnoreCase) && term.Count(character => character == ' ') <= 5)
            .Where(term => EvidenceTerms.Mentions(candidate, term))
            .Concat(EvidenceTerms.FindExplicitConcepts(candidate))
            .Select(EvidenceTerms.Canonicalize).Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();
    }
}
