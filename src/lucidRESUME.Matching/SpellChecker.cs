using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Quality;
using lucidRESUME.Core.Models.Resume;
using WeCantSpell.Hunspell;

namespace lucidRESUME.Matching;

/// <summary>
/// Entity-aware spell checker for resume text.
/// Skips known skills, company names, NER entities, technical terms,
/// abbreviations, and camelCase/PascalCase identifiers.
/// </summary>
public sealed partial class SpellChecker
{
    private static readonly Lazy<WordList?> Dictionary = new(() =>
    {
        var dicPath = Path.Combine(AppContext.BaseDirectory, "Resources", "dictionaries", "en_US.dic");
        var affPath = Path.Combine(AppContext.BaseDirectory, "Resources", "dictionaries", "en_US.aff");
        if (!File.Exists(dicPath) || !File.Exists(affPath))
        {
            // Try assembly location fallback
            var asmDir = Path.GetDirectoryName(typeof(SpellChecker).Assembly.Location)!;
            dicPath = Path.Combine(asmDir, "Resources", "dictionaries", "en_US.dic");
            affPath = Path.Combine(asmDir, "Resources", "dictionaries", "en_US.aff");
        }
        if (File.Exists(dicPath) && File.Exists(affPath))
            return WordList.CreateFromFiles(dicPath, affPath);
        return null;
    });

    /// <summary>
    /// Check spelling across the entire resume, returning findings for misspelled words.
    /// Entity-aware: builds an allow-list from the resume's own skills, companies, entities.
    /// </summary>
    public static IReadOnlyList<QualityFinding> Check(ResumeDocument resume)
    {
        var dict = Dictionary.Value;
        if (dict == null) return [];

        // Build entity-aware allow-list from this resume's own data
        var allowList = BuildAllowList(resume);

        var findings = new List<QualityFinding>();

        // Check achievement bullets (most important — these face ATS and humans)
        for (int j = 0; j < resume.Experience.Count; j++)
        {
            var exp = resume.Experience[j];
            for (int k = 0; k < exp.Achievements.Count; k++)
            {
                var section = $"Experience[{j}].Achievements[{k}]";
                CheckText(dict, allowList, exp.Achievements[k], section, findings);
            }
        }

        // Check summary
        if (!string.IsNullOrWhiteSpace(resume.Personal.Summary))
            CheckText(dict, allowList, resume.Personal.Summary!, "Summary", findings);

        // Check project descriptions
        for (int i = 0; i < resume.Projects.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(resume.Projects[i].Description))
                CheckText(dict, allowList, resume.Projects[i].Description!, $"Projects[{i}]", findings);
        }

        return findings;
    }

    private static HashSet<string> BuildAllowList(ResumeDocument resume)
    {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Skills — all skill names and their individual words
        foreach (var skill in resume.Skills)
        {
            allow.Add(skill.Name);
            foreach (var word in skill.Name.Split([' ', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries))
                allow.Add(word);
        }

        // Technologies from experience
        foreach (var exp in resume.Experience)
        {
            foreach (var tech in exp.Technologies)
            {
                allow.Add(tech);
                foreach (var word in tech.Split([' ', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries))
                    allow.Add(word);
            }
        }

        // Company names
        foreach (var exp in resume.Experience)
        {
            if (!string.IsNullOrEmpty(exp.Company))
            {
                foreach (var word in exp.Company.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    allow.Add(word);
            }
        }

        // NER entities (all extracted entities — names, orgs, locations, etc.)
        foreach (var entity in resume.Entities)
        {
            allow.Add(entity.Value);
            foreach (var word in entity.Value.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries))
                allow.Add(word);
        }

        // Education institutions and degrees
        foreach (var edu in resume.Education)
        {
            if (!string.IsNullOrEmpty(edu.Institution))
                foreach (var word in edu.Institution.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    allow.Add(word);
            if (!string.IsNullOrEmpty(edu.Degree))
                foreach (var word in edu.Degree.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    allow.Add(word);
        }

        // Certification names
        foreach (var cert in resume.Certifications)
        {
            if (!string.IsNullOrEmpty(cert.Name))
                foreach (var word in cert.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    allow.Add(word);
        }

        // Person's own name
        if (!string.IsNullOrEmpty(resume.Personal.FullName))
            foreach (var word in resume.Personal.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                allow.Add(word);

        return allow;
    }

    private static void CheckText(WordList dict, HashSet<string> allowList,
        string text, string section, List<QualityFinding> findings)
    {
        var words = WordSplitRx().Split(text);

        foreach (var raw in words)
        {
            var word = raw.Trim('\'', '"', '(', ')', '[', ']', '{', '}', ',', '.', ';', ':', '!', '?');
            if (word.Length < 3) continue;

            // Skip if in entity allow-list
            if (allowList.Contains(word)) continue;

            // Skip numbers, percentages, URLs, emails, file paths
            if (char.IsDigit(word[0])) continue;
            if (word.Contains('@') || word.Contains("://") || word.Contains('/') || word.Contains('\\')) continue;

            // Skip ALL CAPS (abbreviations: API, SQL, AWS, CTO, etc.)
            if (word.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '-')) continue;

            // Skip camelCase / PascalCase identifiers (JavaScript, ASP.NET, etc.)
            if (word.Length > 1 && word.Any(char.IsUpper) && word.Any(char.IsLower) && MixedCaseRx().IsMatch(word))
                continue;

            // Skip words with digits mixed in (v2, k8s, h264, etc.)
            if (word.Any(char.IsDigit)) continue;

            // Skip hyphenated compounds — check each part individually
            if (word.Contains('-'))
            {
                foreach (var part in word.Split('-'))
                {
                    if (part.Length >= 3 && !allowList.Contains(part) && !dict.Check(part))
                    {
                        var suggestions = dict.Suggest(part).Take(3).ToList();
                        var sugText = suggestions.Count > 0 ? $" — did you mean: {string.Join(", ", suggestions)}?" : "";
                        findings.Add(new(section, FindingSeverity.Warning,
                            "MISSPELLED", $"\"{part}\" (in \"{word}\") may be misspelled{sugText}"));
                    }
                }
                continue;
            }

            // Main check
            if (!dict.Check(word))
            {
                var suggestions = dict.Suggest(word).Take(3).ToList();
                var sugText = suggestions.Count > 0 ? $" — did you mean: {string.Join(", ", suggestions)}?" : "";
                findings.Add(new(section, FindingSeverity.Warning,
                    "MISSPELLED", $"\"{word}\" may be misspelled{sugText}"));
            }
        }
    }

    [GeneratedRegex(@"[\s]+")]
    private static partial Regex WordSplitRx();

    [GeneratedRegex(@"[a-z][A-Z]|[A-Z][a-z].*[A-Z]")]
    private static partial Regex MixedCaseRx();
}
