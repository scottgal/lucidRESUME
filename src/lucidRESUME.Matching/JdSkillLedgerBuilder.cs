using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Builds a JdSkillLedger from a JobDescription.
/// Uses NER to extract clean skill names from the full JD text.
/// Falls back to structural extractor output only for short, clean terms.
/// No regex sentence parsing — NER does the heavy lifting.
/// </summary>
public sealed class JdSkillLedgerBuilder
{
    private readonly IEmbeddingService _embedder;
    private readonly IEnumerable<IEntityDetector>? _detectors;

    public JdSkillLedgerBuilder(IEmbeddingService embedder, IEnumerable<IEntityDetector>? detectors = null)
    {
        _embedder = embedder;
        _detectors = detectors;
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

        // 1. NER: extract clean skill names from full JD text
        if (_detectors is not null && !string.IsNullOrEmpty(jd.RawText))
        {
            var context = new DetectionContext(jd.RawText);
            foreach (var detector in _detectors)
            {
                try
                {
                    var entities = await detector.DetectAsync(context, ct);
                    foreach (var entity in entities.Where(e =>
                        e.Classification is "NerSkill" && e.Value.Length >= 2))
                    {
                        if (!seen.Add(entity.Value)) continue;

                        var importance = IsInSection(jd.RequiredSkills, entity.Value)
                            ? SkillImportance.Required
                            : IsInSection(jd.PreferredSkills, entity.Value)
                                ? SkillImportance.Preferred
                                : SkillImportance.Inferred;

                        ledger.Requirements.Add(new JdSkillRequirement
                        {
                            SkillName = entity.Value,
                            Importance = importance,
                            SourceText = entity.Value,
                            Embedding = await _embedder.EmbedAsync(entity.Value, ct),
                        });
                    }
                }
                catch { /* non-fatal */ }
            }
        }

        // 2. Structural: add terms from the extracted skill lists
        // These come from the JD parser's structural + NER fusion
        // Filter out years requirements but keep everything else
        foreach (var rawSkill in jd.RequiredSkills.Concat(jd.PreferredSkills))
        {
            var clean = rawSkill.Trim();
            if (clean.Length < 2) continue;

            // Skip years-of-experience lines
            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^\d+\+?\s*years")) continue;

            var importance = jd.RequiredSkills.Contains(rawSkill)
                ? SkillImportance.Required : SkillImportance.Preferred;

            if (!seen.Add(clean)) continue;
            ledger.Requirements.Add(new JdSkillRequirement
            {
                SkillName = clean,
                Importance = importance,
                SourceText = rawSkill,
                Embedding = await _embedder.EmbedAsync(clean, ct),
            });
        }

        return ledger;
    }

    private static bool IsInSection(IReadOnlyList<string> section, string skill) =>
        section.Any(s => s.Contains(skill, StringComparison.OrdinalIgnoreCase));
}
