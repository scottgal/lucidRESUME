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

        // Required skills — explicit
        foreach (var skill in jd.RequiredSkills.Where(s => s.Length > 1))
        {
            var normalized = skill.Trim();
            if (!seen.Add(normalized)) continue;
            ledger.Requirements.Add(new JdSkillRequirement
            {
                SkillName = normalized,
                Importance = SkillImportance.Required,
                SourceText = skill,
                Embedding = await _embedder.EmbedAsync(normalized, ct),
            });
        }

        // Preferred skills — nice-to-have
        foreach (var skill in jd.PreferredSkills.Where(s => s.Length > 1))
        {
            var normalized = skill.Trim();
            if (!seen.Add(normalized)) continue;
            ledger.Requirements.Add(new JdSkillRequirement
            {
                SkillName = normalized,
                Importance = SkillImportance.Preferred,
                SourceText = skill,
                Embedding = await _embedder.EmbedAsync(normalized, ct),
            });
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
