using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>Projects persisted advert requirements. Matching never reruns extraction or bypasses classification.</summary>
public sealed class JdSkillLedgerBuilder
{
    private readonly IEmbeddingService embedder;
    public JdSkillLedgerBuilder(IEmbeddingService embedder, IEnumerable<IEntityDetector>? detectors = null)
    {
        this.embedder = embedder;
        // Retain the constructor contract for hosts; extraction belongs to job ingestion.
        _ = detectors;
    }
    public async Task<JdSkillLedger> BuildAsync(JobDescription jd, CancellationToken ct = default)
    {
        var ledger = new JdSkillLedger { JobId = jd.JobId, JobTitle = jd.Title, Company = jd.Company };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classified = !string.IsNullOrWhiteSpace(jd.SkillAnalysisVersion) || jd.SkillDecisions.Count > 0;
        var allowed = jd.SkillDecisions.Where(decision => decision.Classification is "skill" or "capability")
            .Select(decision => decision.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        async Task AddAsync(string name, string source, SkillImportance importance, bool competency = false)
        {
            ct.ThrowIfCancellationRequested();
            var clean = name.Trim().TrimStart('-', '*', '•', ' ');
            if (clean.Length == 0) return;
            if (!competency && classified && !allowed.Contains(name) && !allowed.Contains(clean)) return;
            if (!competency && !string.IsNullOrWhiteSpace(jd.RawText) && !EvidenceTerms.Mentions(jd.RawText, clean)) return;
            var literalCanonical = EvidenceTerms.Canonicalize(clean);
            var canonical = SkillTaxonomy.Canonicalize(literalCanonical) ?? literalCanonical;
            if (!seen.Add(canonical)) return;
            ledger.Requirements.Add(new JdSkillRequirement
            {
                SkillName = clean, Importance = importance, SourceText = source,
                Embedding = await embedder.EmbedAsync(source, ct)
            });
        }
        if (jd.Competencies.Count > 0)
        {
            foreach (var competency in jd.Competencies.OrderBy(item => item.Priority))
                await AddAsync(competency.Name, competency.Description, competency.Priority switch
                {
                    RequirementPriority.Required => SkillImportance.Required,
                    RequirementPriority.Preferred => SkillImportance.Preferred,
                    _ => SkillImportance.Inferred
                }, competency: true);
        }
        else
        {
            foreach (var skill in jd.RequiredSkills) await AddAsync(skill, SourceFor(skill), SkillImportance.Required);
            foreach (var skill in jd.PreferredSkills) await AddAsync(skill, SourceFor(skill), SkillImportance.Preferred);
        }
        foreach (var skill in jd.MentionedSkills) await AddAsync(skill, SourceFor(skill), SkillImportance.Inferred);
        return ledger;

        string SourceFor(string name) => jd.SkillDecisions.FirstOrDefault(decision =>
            decision.Label.Equals(name, StringComparison.OrdinalIgnoreCase))?.SourceText ?? name;
    }
}
