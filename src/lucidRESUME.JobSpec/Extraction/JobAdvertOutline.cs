using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using System.Text.RegularExpressions;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>Retains competency paragraphs in sectioned adverts instead of splitting every comma into a skill.</summary>
internal static class JobAdvertOutline
{
    internal static string? SectionKind(string line)
    {
        var text = line.Trim().TrimStart('#').Replace("**", "").Trim().TrimEnd(':').ToLowerInvariant()
            .Replace('’', '\'');
        return text switch
        {
            "what we're looking for" or "what we are looking for" or "requirements" or "required qualifications" or
            "minimum qualifications" or "qualifications" or "what you'll bring" => "required",
            "what sets you apart" or "nice to have" or "nice-to-have" or "preferred qualifications" or
            "preferred skills" or "bonus" or "desirable" => "preferred",
            "what you'll own" or "what you will own" or "what you'll do" or "what you will do" or
            "responsibilities" or "key responsibilities" => "responsibility",
            "about the role" or "about us" or "about the company" or "the environment" or
            "location and working style" or "location" or "working style" or "benefits" or "compensation" or
            "compensation and benefits" or "what we offer" or "how to apply" or "equal opportunities" => "ignore",
            _ => null
        };
    }

    internal static List<JobCompetency> Extract(string text)
    {
        var result = new List<JobCompetency>();
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
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var kind = SectionKind(line);
            if (kind is not null) { Flush(); section = kind; continue; }
            if (section is null or "ignore") continue;
            var clean = line.TrimStart('#', '-', '*', '•', ' ').Replace("**", "").Trim();
            if (section == "preferred")
            {
                // Distinguishing experience is optional, including domain and specialist-model work.
                result.Add(new(clean.TrimEnd('.'), clean, RequirementPriority.Preferred));
                continue;
            }
            var headingShaped = clean.Length is > 3 and < 85 && !clean.EndsWith('.') &&
                !clean.Contains(':') && !raw.TrimStart().StartsWith('-') &&
                clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10;
            if (headingShaped) { Flush(); heading = clean; }
            else if (heading is not null) prose.Add(clean);
        }
        Flush();
        // Use this representation only when the advert really provides multiple described competencies.
        return result.Count(x => x.Priority == RequirementPriority.Required) >= 2 &&
               result.Count(x => x.Priority == RequirementPriority.Responsibility) >= 2 ? result : [];
    }
}
