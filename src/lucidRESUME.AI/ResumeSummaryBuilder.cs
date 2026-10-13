using System.Text.RegularExpressions;
using lucidRESUME.Matching;

namespace lucidRESUME.AI;

/// <summary>A conservative introduction for offline use and grounded summary editing.</summary>
internal static class ResumeSummaryBuilder
{
    internal sealed record FocusAccount(IReadOnlyList<string> Sources, string Phrase, IReadOnlyList<string> RemainingSkills);

    public static bool IsUnqualifiedAccount(string text) => !Regex.IsMatch(text,
        @"\b(helped|assisted|supported|contributed|prototype|pilot|unknown|not|never|planned|proposed|intended|cancelled|canceled|estimated|approximately|might|may|would|hoped)\b|\b(?:up to|no longer)\b", RegexOptions.IgnoreCase);

    internal static string? FocusPhrase(string source)
    {
        if (!IsUnqualifiedAccount(source)) return null;
        var match = Regex.Match(source,
            @"^(Built|Designed|Maintained|Implemented|Developed|Reviewed|Curated|Optimised|Optimized|Added)\s+(.+?)(?=[;:!?]|\.(?:\s|$)|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        // Keep a short action/object theme. Tool lists, coordinated duties, purposes and results stay in Experience.
        var subject = Regex.Split(match.Groups[2].Value, @"\s+\b(?:using|with|and|for|before|so|that|which|to)\b", RegexOptions.IgnoreCase)[0].Trim();
        if (SkillTaxonomy.Canonicalize(Regex.Replace(subject, @"^(?:a|an|the)\s+", "", RegexOptions.IgnoreCase)) is not null)
            subject = Regex.Split(match.Groups[2].Value, @"\s+\b(?:using|with|for|before|so|that|which|to)\b", RegexOptions.IgnoreCase)[0].Trim();
        if (subject.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 7) return null;
        var action = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "built" => "building",
            "designed" => "designing",
            "maintained" => "maintaining",
            "implemented" => "implementing",
            "developed" => "developing",
            "reviewed" => "reviewing",
            "curated" => "curating",
            "added" => "adding",
            _ => "optimising"
        };
        return action + " " + subject;
    }

    public static FocusAccount? FindFocusAccount(IEnumerable<string> accounts, IReadOnlyList<string> skills)
    {
        if (skills.Count == 0) return null;
        var selected = accounts.Select(source => (Source: source, Phrase: FocusPhrase(source)))
            .Where(item => item.Phrase is not null).DistinctBy(item => item.Phrase).Take(2).ToList();
        if (selected.Count == 0 || !skills.All(skill => selected.Any(item => SkillTaxonomy.MentionsEquivalent(item.Source, skill)))) return null;
        var phrases = selected.Select(item => item.Phrase!).ToList();
        if (phrases.Count == 2)
            foreach (var skill in skills.Where(skill => SkillTaxonomy.MentionsEquivalent(phrases[0], skill)))
            {
                var shorter = Regex.Replace(phrases[1], @"^(?<action>\S+)\s+" + Regex.Escape(skill) + @"\s+", "${action} ", RegexOptions.IgnoreCase);
                shorter = Regex.Replace(shorter, @"\s+", " ").Trim();
                if (shorter.Split(' ').Length >= 2 && !Regex.IsMatch(shorter, @"^\S+\s+(?:and|or|with|using)\b|\b(?:a|an|the|with|using)$", RegexOptions.IgnoreCase)) phrases[1] = shorter;
            }
        var phrase = string.Join(" and ", phrases);
        return new(selected.Select(item => item.Source).ToList(), phrase,
            skills.Where(skill => !SkillTaxonomy.MentionsEquivalent(phrase, skill)).ToList());
    }

    public static string Build(string careerSummary, IReadOnlyList<string> focusSkills, bool retainCareerSummary, FocusAccount? account = null)
    {
        if (retainCareerSummary || focusSkills.Count == 0) return careerSummary;
        var identity = Identity(careerSummary);
        if (account is not null)
        {
            var summary = identity is null ? $"Experience {account.Phrase}." : $"{identity} with experience {account.Phrase}.";
            return account.RemainingSkills.Count == 0 ? summary : summary + $" Technical experience includes {Join(account.RemainingSkills)}.";
        }
        return identity is null ? $"Experience with {Join(focusSkills)}." : $"{identity} with experience in {Join(focusSkills)}.";
    }

    private static string Join(IReadOnlyList<string> values) => values.Count == 1 ? values[0]
        : string.Join(", ", values.Take(values.Count - 1)) + " and " + values[^1];

    public static bool UsesIdentity(string summary, string source) =>
        Identity(source) is { } identity && summary.StartsWith(identity + " with experience ", StringComparison.Ordinal);

    internal static string? Identity(string source)
    {
        // Only reuse an explicitly stated professional identity, never a title inferred from the vacancy.
        var prefix = Regex.Split(source, @"\s+\b(?:building|developing|delivering|designing|maintaining|working|specialising|specializing|focused|with)\b",
            RegexOptions.IgnoreCase)[0].Trim().TrimEnd('.');
        return prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10 &&
               Regex.IsMatch(prefix, @"\b(?:engineer|developer|architect|leader|consultant|manager|designer)\b$", RegexOptions.IgnoreCase)
            ? prefix : null;
    }
}
