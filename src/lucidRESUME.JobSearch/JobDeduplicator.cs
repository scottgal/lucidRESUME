using lucidRESUME.Core.Models.Jobs;
using System.Text.RegularExpressions;

namespace lucidRESUME.JobSearch;

public sealed class JobDeduplicator
{
    private static readonly string[] StripWords =
        ["senior", "junior", "lead", "principal", "staff", "mid", "associate"];

    private static readonly Regex StripPattern = new(
        $@"\b({string.Join("|", StripWords)})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string NormaliseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        var normalised = StripPattern.Replace(title.ToLowerInvariant(), "");
        // Collapse multiple spaces left by removed words
        return Regex.Replace(normalised.Trim(), @"\s{2,}", " ");
    }

    public IReadOnlyList<JobDescription> Deduplicate(IEnumerable<JobDescription> jobs)
    {
        return jobs
            .GroupBy(j => $"{(j.Company ?? "").ToLowerInvariant()}|{NormaliseTitle(j.Title)}")
            .Select(g => g.OrderByDescending(j => j.Source.FetchedAt).First())
            .ToList();
    }
}
