using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Builds a JdSkillLedger from a JobDescription.
/// Extracts skills from required, preferred, and description text.
/// </summary>
public sealed class JdSkillLedgerBuilder
{
    private readonly IEmbeddingService _embedder;

    public JdSkillLedgerBuilder(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    public async Task<JdSkillLedger> BuildAsync(JobDescription jd, CancellationToken ct = default)
    {
        var ledger = new JdSkillLedger
        {
            JobId = jd.JobId,
            JobTitle = jd.Title,
            Company = jd.Company,
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Required skills — extract individual skill terms from requirement sentences
        foreach (var rawSkill in jd.RequiredSkills.Where(s => s.Length > 1))
        {
            var terms = ExtractSkillTerms(rawSkill);
            foreach (var term in terms)
            {
                if (!seen.Add(term)) continue;
                ledger.Requirements.Add(new JdSkillRequirement
                {
                    SkillName = term,
                    Importance = SkillImportance.Required,
                    SourceText = rawSkill,
                    Embedding = await _embedder.EmbedAsync(term, ct),
                });
            }
        }

        // Preferred skills
        foreach (var rawSkill in jd.PreferredSkills.Where(s => s.Length > 1))
        {
            var terms = ExtractSkillTerms(rawSkill);
            foreach (var term in terms)
            {
                if (!seen.Add(term)) continue;
                ledger.Requirements.Add(new JdSkillRequirement
                {
                    SkillName = term,
                    Importance = SkillImportance.Preferred,
                    SourceText = rawSkill,
                    Embedding = await _embedder.EmbedAsync(term, ct),
                });
            }
        }

        // Inferred from description — skills mentioned in responsibilities/benefits
        // that aren't already in required/preferred
        foreach (var resp in jd.Responsibilities)
        {
            var inferredSkills = ExtractTechTerms(resp);
            foreach (var skill in inferredSkills)
            {
                if (!seen.Add(skill)) continue;
                ledger.Requirements.Add(new JdSkillRequirement
                {
                    SkillName = skill,
                    Importance = SkillImportance.Inferred,
                    SourceText = resp,
                    Embedding = await _embedder.EmbedAsync(skill, ct),
                });
            }
        }

        return ledger;
    }

    /// <summary>
    /// Extract individual skill terms from a requirement sentence.
    /// "Production experience with Azure (AKS, Functions, Service Bus, Cosmos DB)"
    /// → ["Azure", "AKS", "Functions", "Service Bus", "Cosmos DB"]
    /// "10+ years software engineering" → [] (years requirement, not a skill)
    /// </summary>
    private static List<string> ExtractSkillTerms(string requirement)
    {
        var terms = new List<string>();
        var text = requirement.Trim();

        // Skip years-of-experience lines
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\+?\s*years"))
            return terms;

        // If it's short enough to be a single skill/term (< 40 chars, no verbs), keep as-is
        if (text.Length < 40 && !ContainsVerbPhrase(text))
        {
            terms.Add(text);
            return terms;
        }

        // Extract parenthetical content as separate skills
        // "Azure (AKS, Functions, Service Bus)" → "Azure" + "AKS" + "Functions" + "Service Bus"
        var parenMatch = System.Text.RegularExpressions.Regex.Match(text, @"(\w[\w\s.#/+-]*?)\s*\(([^)]+)\)");
        if (parenMatch.Success)
        {
            var mainTerm = parenMatch.Groups[1].Value.Trim();
            if (mainTerm.Length > 1) terms.Add(mainTerm);
            var inner = parenMatch.Groups[2].Value;
            foreach (var part in inner.Split([',', ';'], StringSplitOptions.TrimEntries))
            {
                var clean = part.Trim();
                if (clean.Length > 1) terms.Add(clean);
            }
        }

        // Extract terms joined by "and" / "or"
        // "Kubernetes and Docker" → ["Kubernetes", "Docker"]
        foreach (var segment in text.Split([" and ", " or ", " / "], StringSplitOptions.TrimEntries))
        {
            // Strip common prefixes: "Deep expertise in", "Production experience with", "Strong"
            var stripped = StripQualifiers(segment);
            if (stripped.Length > 1 && stripped.Length < 50 && !terms.Any(t => t.Equals(stripped, StringComparison.OrdinalIgnoreCase)))
                terms.Add(stripped);
        }

        // If nothing extracted, use the whole thing (better than nothing)
        if (terms.Count == 0 && text.Length < 80)
            terms.Add(text);

        return terms;
    }

    private static string StripQualifiers(string text)
    {
        ReadOnlySpan<string> prefixes =
        [
            "deep expertise in", "production experience with", "strong",
            "experience with", "proficiency in", "knowledge of",
            "experience in", "familiarity with", "understanding of",
        ];
        var lower = text.ToLowerInvariant().Trim();
        foreach (var prefix in prefixes)
        {
            if (lower.StartsWith(prefix))
                return text[prefix.Length..].TrimStart(' ', ',');
        }
        return text.Trim();
    }

    private static bool ContainsVerbPhrase(string text)
    {
        var lower = text.ToLowerInvariant();
        ReadOnlySpan<string> verbs = ["experience", "expertise", "proficiency", "knowledge", "understanding", "years"];
        foreach (var v in verbs)
            if (lower.Contains(v)) return true;
        return false;
    }

    /// <summary>
    /// Extract likely tech terms from a text line.
    /// Simple heuristic: words that contain dots, hashes, plusses, or are ALL CAPS short words.
    /// This catches "C#", ".NET", "ASP.NET", "K8s", "CI/CD" etc.
    /// </summary>
    private static List<string> ExtractTechTerms(string text)
    {
        var terms = new List<string>();
        var words = text.Split([' ', ',', ';', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var clean = word.Trim('.', ',', ';', ':');
            if (clean.Length < 2) continue;

            // Tech indicators: contains special chars, or is a known pattern
            if (clean.Contains('#') || clean.Contains('.') || clean.Contains('+') ||
                clean.Contains('/') ||
                (clean.Length <= 6 && clean == clean.ToUpperInvariant() && clean.Any(char.IsLetter)))
            {
                terms.Add(clean);
            }
        }

        return terms;
    }
}
