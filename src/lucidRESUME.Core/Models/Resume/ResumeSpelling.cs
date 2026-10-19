using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>Reviewed ordinary-word corrections for rendered prose; original evidence is retained verbatim.</summary>
public static class ResumeSpelling
{
    private static readonly IReadOnlyDictionary<string, string> Corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["distrubted"] = "distributed", ["applicaiton"] = "application",
        ["architrcture"] = "architecture", ["implentation"] = "implementation", ["hundfreds"] = "hundreds"
    };
    public static string CorrectWord(string word) => Corrections.GetValueOrDefault(word, word);
    public static string Correct(string text) => Regex.Replace(text, @"\b[\p{L}]+\b", match =>
    {
        if (!Corrections.TryGetValue(match.Value, out var correction)) return match.Value;
        return char.IsUpper(match.Value[0]) ? char.ToUpperInvariant(correction[0]) + correction[1..] : correction;
    });
}
