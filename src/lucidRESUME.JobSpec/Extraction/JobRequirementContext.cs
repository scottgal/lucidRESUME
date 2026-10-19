using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Evidence;

namespace lucidRESUME.JobSpec.Extraction;

internal static class JobRequirementContext
{
    private static IEnumerable<(string Text, string Section)> Lines(string text) =>
        lucidRESUME.Core.Models.Jobs.JobTextSections.Read(text).Select(line => (line.Text, line.Section));
    private static bool Matches(string text, string label) => EvidenceTerms.Mentions(text, label) &&
        (label is not ("SOLID" or "SAFe") || Regex.IsMatch(text, @"\b" + label + @"\b"));
    private static int Rank(string section) => section switch { "required" => 0, "preferred" => 1, "responsibility" => 2, "unspecified" => 3, _ => 4 };
    internal static string? Priority(string text, string label) => Lines(text).Where(line => Matches(line.Text, label))
        .OrderBy(line => Rank(line.Section)).Select(line => line.Section).FirstOrDefault();
    internal static bool HasRequiredSection(string text) => Lines(text).Any(line => line.Section == "required");
    internal static string? Excerpt(string text, string label)
    {
        var line = Lines(text).Where(line => Matches(line.Text, label)).OrderBy(line => Rank(line.Section)).Select(line => line.Text).FirstOrDefault();
        if (line is null) line = Lines(text).FirstOrDefault(line => line.Text.Contains(label, StringComparison.OrdinalIgnoreCase)).Text;
        if (line is null || line.Length <= 500) return line;
        var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, index - 100);
        return line.Substring(start, Math.Min(500, line.Length - start));
    }
    internal static bool IsLiteral(string text, string label) => Matches(text, label);
}
