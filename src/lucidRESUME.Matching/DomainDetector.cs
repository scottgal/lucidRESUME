using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

/// <summary>
/// Detects the professional domain/industry of a resume based on its content.
/// Uses skill names, job titles, and section text to classify into one or more domains.
/// This enables loading the right taxonomies for accurate matching.
/// </summary>
public sealed class DomainDetector
{
    private static readonly Lazy<Dictionary<string, DomainSignals>> DomainSignalMap = new(BuildSignalMap);
    /// <summary>
    /// Detect the top domains for a resume, ranked by confidence.
    /// Uses keyword-based detection from taxonomy files.
    /// </summary>
    public static IReadOnlyList<DomainMatch> Detect(ResumeDocument resume)
    {
        var scores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Collect all text signals from the resume
        var skills = resume.Skills.Select(s => s.Name.ToLowerInvariant()).ToHashSet();
        var titles = resume.Experience.Select(e => (e.Title ?? "").ToLowerInvariant()).ToList();
        var allText = (resume.PlainText ?? resume.RawMarkdown ?? "").ToLowerInvariant();

        foreach (var (domain, signals) in DomainSignalMap.Value)
        {
            float score = 0;

            // Skill matches (strongest signal)
            foreach (var skillKw in signals.SkillKeywords)
            {
                if (skills.Any(s => s.Contains(skillKw)))
                    score += 3f;
            }

            // Title matches (strong signal)
            foreach (var titleKw in signals.TitleKeywords)
            {
                if (titles.Any(t => t.Contains(titleKw)))
                    score += 5f;
            }

            // Text mentions (weak signal but broad)
            foreach (var textKw in signals.TextKeywords)
            {
                if (allText.Contains(textKw))
                    score += 1f;
            }

            if (score > 0)
                scores[domain] = score;
        }

        if (scores.Count == 0)
            return [new DomainMatch("general", 0.5f)];

        var maxScore = scores.Values.Max();
        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => new DomainMatch(kv.Key, Math.Min(kv.Value / maxScore, 1f)))
            .ToList();
    }

    /// <summary>
    /// Get the primary domain for a resume.
    /// </summary>
    public static string DetectPrimary(ResumeDocument resume)
    {
        var domains = Detect(resume);
        return domains.Count > 0 ? domains[0].Domain : "general";
    }

    private static Dictionary<string, DomainSignals> BuildSignalMap()
    {
        // Load from taxonomy files — each taxonomy filename IS the domain
        var signals = new Dictionary<string, DomainSignals>(StringComparer.OrdinalIgnoreCase);

        var taxDir = Path.Combine(AppContext.BaseDirectory, "Resources", "taxonomies");
        if (!Directory.Exists(taxDir))
        {
            var asmDir = Path.GetDirectoryName(typeof(DomainDetector).Assembly.Location)!;
            taxDir = Path.Combine(asmDir, "Resources", "taxonomies");
        }
        if (!Directory.Exists(taxDir)) return signals;

        foreach (var file in Directory.GetFiles(taxDir, "*.txt"))
        {
            var domain = Path.GetFileNameWithoutExtension(file);
            var skillKws = new List<string>();
            var titleKws = new List<string>();
            var textKws = new List<string>();

            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var colonIdx = line.IndexOf(':');
                if (colonIdx <= 0) continue;

                var canonical = line[..colonIdx].Trim().ToLowerInvariant();
                var aliases = line[(colonIdx + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant())
                    .Where(a => a.Length > 0)
                    .ToList();

                // The canonical term is a skill keyword
                skillKws.Add(canonical);
                // Short aliases are also good skill keywords
                skillKws.AddRange(aliases.Where(a => a.Length >= 2 && a.Length <= 25));
            }

            // Derive title keywords from domain name
            titleKws.Add(domain.Replace("-", " "));
            // Add common title patterns per domain
            titleKws.AddRange(domain switch
            {
                "information-technology" => ["developer", "engineer", "architect", "devops", "sysadmin", "programmer", "sre"],
                "healthcare" => ["nurse", "physician", "therapist", "pharmacist", "clinical", "medical"],
                "finance" => ["analyst", "accountant", "controller", "treasurer", "auditor", "banker"],
                "engineering" => ["engineer", "technician", "drafter", "designer"],
                "sales" => ["sales", "account executive", "business development", "representative"],
                "education" or "teacher" => ["teacher", "instructor", "professor", "tutor", "lecturer"],
                "hr" => ["recruiter", "hr ", "human resources", "talent", "people ops"],
                "construction" => ["foreman", "superintendent", "estimator", "project manager"],
                "design" => ["designer", "creative", "art director", "ux", "ui"],
                "digital-media" => ["marketing", "seo", "content", "social media", "copywriter"],
                "hospitality" => ["chef", "cook", "server", "bartender", "hotel", "restaurant"],
                "legal" => ["attorney", "lawyer", "paralegal", "counsel", "legal"],
                "banking" => ["banker", "teller", "loan", "credit", "branch"],
                "accounting" => ["accountant", "bookkeeper", "auditor", "cpa", "controller"],
                "aviation" => ["pilot", "mechanic", "avionics", "flight", "aircraft"],
                _ => Array.Empty<string>()
            });

            textKws.AddRange(skillKws.Where(k => k.Length >= 4).Take(20));

            signals[domain] = new DomainSignals(
                skillKws.Distinct().ToList(),
                titleKws.Distinct().ToList(),
                textKws.Distinct().ToList());
        }

        return signals;
    }

    private record DomainSignals(List<string> SkillKeywords, List<string> TitleKeywords, List<string> TextKeywords);
}

public record DomainMatch(string Domain, float Confidence);
