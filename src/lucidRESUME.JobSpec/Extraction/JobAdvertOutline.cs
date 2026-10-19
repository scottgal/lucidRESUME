using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using System.Text.RegularExpressions;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>Retains competency paragraphs in sectioned adverts instead of splitting every comma into a skill.</summary>
internal static class JobAdvertOutline
{
    internal static string? SectionKind(string line) => JobTextSections.SectionKind(line);

    internal static List<JobCompetency> Extract(string text)
    {
        var result = new List<JobCompetency>();
        var namedRequired = 0;
        string? section = null, heading = null;
        var prose = new List<string>();
        void Flush()
        {
            if (section is "required" or "responsibility" && heading is not null && prose.Count > 0)
                result.Add(new(heading, string.Join(" ", prose), section == "required"
                    ? RequirementPriority.Required : RequirementPriority.Responsibility));
            heading = null;
            prose.Clear();
        }
        var normalized = Regex.Replace(text, @"(?im)^(\s*(?:essential|nice[- ]to[- ]have))\s+(?=\S)", "$1\n");
        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var kind = SectionKind(line);
            if (kind is not null) { Flush(); section = kind; continue; }
            // Career-path alternatives explain who may apply; they are not mandatory experience.
            if (heading is null && Regex.IsMatch(line, @"^You (?:might be|could also be)\b", RegexOptions.IgnoreCase))
            { section = "ignore"; continue; }
            if (section is null or "ignore") continue;
            var clean = line.TrimStart('#', '-', '*', '•', ' ').Replace("**", "").Trim();
            if (section == "preferred" || Regex.IsMatch(clean, @"^(?:preferably|ideally|bonus|desirable)\b", RegexOptions.IgnoreCase))
            {
                // Distinguishing experience is optional, including domain and specialist-model work.
                result.Add(new(clean.TrimEnd('.'), clean, RequirementPriority.Preferred));
                continue;
            }
            var headingShaped = clean.Length is > 3 and < 85 && !clean.EndsWith('.') &&
                !clean.Contains(':') && !raw.TrimStart().StartsWith('-') &&
                clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10;
            if (headingShaped) { Flush(); heading = clean; if (section == "required") namedRequired++; }
            else if (heading is not null) prose.Add(clean);
            else if (clean.Length > 15)
                result.Add(new(clean.TrimEnd('.'), clean, section == "required"
                    ? RequirementPriority.Required : RequirementPriority.Responsibility));
        }
        Flush();
        // Use this representation only when the advert really provides multiple described competencies.
        var separateAccounts = namedRequired >= 2 || result.Where(item => item.Priority == RequirementPriority.Required)
            .All(item => Regex.Matches(item.Description, @"[.!?]\s+(?=[A-Z])").Count <= 1);
        return separateAccounts && result.Count(x => x.Priority == RequirementPriority.Required) >= 2 &&
               result.Count(x => x.Priority == RequirementPriority.Responsibility) >= 2 ? result : [];
    }
}
