using System.Text.RegularExpressions;

namespace lucidRESUME.Compiler;

/// <summary>Conservative directed measurements, not general numerical entailment.</summary>
public static class ProseNumericTransitions
{
    private const string Value = @"\d+(?:[.,]\d+)*";
    private const string Unit = @"(?:milliseconds|seconds|megabytes|gigabytes|kilobytes|ms|MB|GB|KB|s|%)(?![\p{L}])";
    private static readonly Regex FromTo = new(@"(?i:\bfrom)\s+(?<a>" + Value + @")\s*(?<au>" + Unit + @")?\s+(?i:to)\s+(?<b>" + Value + @")\s*(?<bu>" + Unit + @")?", RegexOptions.Compiled);
    private static readonly Regex ToFrom = new(@"(?i:\bto)\s+(?<b>" + Value + @")\s*(?<bu>" + Unit + @")?\s+(?i:from)\s+(?<a>" + Value + @")\s*(?<au>" + Unit + @")?", RegexOptions.Compiled);

    public static IReadOnlyList<Transition> Extract(string text) => FromTo.Matches(text).Concat(ToFrom.Matches(text))
        .Select(match =>
        {
            var aUnit = CanonicalUnit(match.Groups["au"].Value);
            var bUnit = CanonicalUnit(match.Groups["bu"].Value);
            return new Transition(match.Groups["a"].Value.Replace(",", ""), aUnit.Length > 0 ? aUnit : bUnit,
                match.Groups["b"].Value.Replace(",", ""), bUnit.Length > 0 ? bUnit : aUnit);
        }).ToArray();

    private static string CanonicalUnit(string unit) => unit switch
    {
        "milliseconds" => "ms",
        "seconds" => "s",
        "megabytes" => "MB",
        "gigabytes" => "GB",
        "kilobytes" => "KB",
        _ => unit
    };

    public sealed record Transition(string From, string FromUnit, string To, string ToUnit);
}
