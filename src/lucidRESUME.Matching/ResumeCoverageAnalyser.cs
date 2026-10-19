using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace lucidRESUME.Matching;

public sealed class ResumeCoverageAnalyser : ICoverageAnalyser
{
    private readonly CompanyClassifier _classifier;
    private readonly IEmbeddingService? _embedder;
    private readonly CoverageOptions _options;

    public ResumeCoverageAnalyser(CompanyClassifier classifier,
        IOptions<CoverageOptions> options,
        IEmbeddingService? embedder = null)
    {
        _classifier = classifier;
        _options = options.Value;
        _embedder = embedder;
    }

    public async Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default)
    {
        var vectors = new Dictionary<string, Task<float[]>>(StringComparer.Ordinal);
        Task<float[]> EmbedOnceAsync(string text)
        {
            if (vectors.TryGetValue(text, out var vector)) return vector;
            return vectors[text] = _embedder!.EmbedAsync(text, ct);
        }
        var companyType = _classifier.Classify(job);
        job.CompanyType = companyType;
        var sourceLedger = Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(resume);
        bool Accepted(string locator) => sourceLedger.Claims.Any(claim => claim.Review == "accepted" &&
            claim.Id.EndsWith(":" + locator, StringComparison.Ordinal));

        var skillEvidence = resume.Skills
            .Select((s, i) => (Text: s.Name, Section: $"Skills[{i}]"))
            .Where(item => Accepted(Core.Models.Evidence.EvidenceLedgerBuilder.SkillLocator(item.Text)))
            .ToList();

        var techEvidence = resume.Experience
            .SelectMany((e, ei) => e.Technologies.Select((t, ti) =>
                (Text: t, Section: $"Experience[{ei}].Technologies[{ti}]", Locator: $"experience:{e.Id:N}:technology:{ti + 1}")))
            .Where(item => Accepted(item.Locator)).Select(item => (item.Text, item.Section))
            .ToList();

        var achievementEvidence = resume.Experience
            .SelectMany((e, ei) => e.Achievements.Select((a, ai) =>
                (Text: a, Section: $"Experience[{ei}].Achievements[{ai}]", Locator: $"experience:{e.Id:N}:achievement:{ai + 1}")))
            .Where(item => Accepted(item.Locator)).Select(item => (item.Text, item.Section))
            .ToList();

        // A role account is more useful for authoring than a bare technology label.
        var allSkillEvidence = achievementEvidence.Concat(techEvidence).Concat(skillEvidence).ToList();

        var entries = new List<CoverageEntry>();
        if (job.Competencies.Count > 0)
        {
            foreach (var competency in job.Competencies.OrderBy(c => c.Priority))
            {
                var reviewed = resume.Experience.SelectMany((role, ri) => role.InterviewEntries
                    .Where(answer => answer.Requirement.Equals(competency.Name, StringComparison.OrdinalIgnoreCase) &&
                                     role.Achievements.Contains(answer.ReviewedProse, StringComparer.Ordinal) &&
                                     Accepted($"experience:{role.Id:N}:achievement:{role.Achievements.IndexOf(answer.ReviewedProse) + 1}"))
                    .Select(answer => (answer.ReviewedProse, Section: $"Experience[{ri}].Achievements[{role.Achievements.IndexOf(answer.ReviewedProse)}]")))
                    .FirstOrDefault();
                if (reviewed != default)
                    entries.Add(new CoverageEntry(new(competency.Name, competency.Priority), reviewed.ReviewedProse,
                        reviewed.Section, 1)
                    { IsDirectMatch = true });
                else
                    entries.Add(await MatchSkillAsync(competency.Name, competency.Priority, achievementEvidence, ct, EmbedOnceAsync,
                        competency.Name + ": " + competency.Description));
            }
            return new CoverageReport(entries.AsReadOnly(), companyType, DateTimeOffset.UtcNow);
        }


        foreach (var req in job.RequiredSkills)
            entries.Add(await MatchSkillAsync(req, RequirementPriority.Required, allSkillEvidence, ct, EmbedOnceAsync));

        foreach (var pref in job.PreferredSkills)
            entries.Add(await MatchSkillAsync(pref, RequirementPriority.Preferred, allSkillEvidence, ct, EmbedOnceAsync));

        foreach (var resp in job.Responsibilities)
            entries.Add(await MatchResponsibilityAsync(resp, achievementEvidence, ct, EmbedOnceAsync));

        return new CoverageReport(entries.AsReadOnly(), companyType, DateTimeOffset.UtcNow);
    }

    private async Task<CoverageEntry> MatchSkillAsync(
        string requirement,
        RequirementPriority priority,
        IReadOnlyList<(string Text, string Section)> evidence,
        CancellationToken ct, Func<string, Task<float[]>> embed, string? retrievalText = null)
    {
        var req = new JdRequirement(requirement, priority);

        // 1. Exact (case-insensitive) match
        var exact = evidence.FirstOrDefault(e => SkillTaxonomy.MentionsEquivalent(e.Text, requirement));
        if (exact != default)
            return new CoverageEntry(req, exact.Text, exact.Section, 1.0f) { IsDirectMatch = true };

        // 2. Semantic match if embedder available
        if (_embedder is not null)
        {
            try
            {
                var reqVec = await embed(retrievalText ?? requirement);
                float bestScore = 0f;
                (string Text, string Section) bestMatch = default;

                foreach (var e in evidence)
                {
                    var eVec = await embed(e.Text);
                    var score = _embedder.CosineSimilarity(reqVec, eVec);
                    if (score > bestScore) { bestScore = score; bestMatch = e; }
                }

                if (bestScore >= (retrievalText is null ? _options.SkillSemanticThreshold : .3f))
                    return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, bestScore);
            }
            catch
            {
                // Fall through to gap
            }
        }

        return new CoverageEntry(req, null, null, 0f);
    }

    private async Task<CoverageEntry> MatchResponsibilityAsync(
        string responsibility,
        IReadOnlyList<(string Text, string Section)> achievements,
        CancellationToken ct, Func<string, Task<float[]>> embed)
    {
        var req = new JdRequirement(responsibility, RequirementPriority.Responsibility);

        var stopWords = _options.StopWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keywords = responsibility.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', '(', ')').ToLowerInvariant())
            .Where(w => w.Length > _options.MinKeywordLength && !stopWords.Contains(w))
            .ToHashSet();

        (string Text, string Section) bestMatch = default;
        int bestOverlap = 0;

        foreach (var ach in achievements)
        {
            var achLower = ach.Text.ToLowerInvariant();
            int overlap = keywords.Count(k =>
                achLower.Contains(k) ||
                (k.EndsWith('s') && k.Length > _options.MinKeywordLength && achLower.Contains(k[..^1])));
            if (overlap > bestOverlap) { bestOverlap = overlap; bestMatch = ach; }
        }

        if (bestOverlap >= _options.MinKeywordOverlap)
        {
            var score = Math.Min(1f, bestOverlap / (float)Math.Max(keywords.Count, 1));
            return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, score);
        }

        // Semantic fallback
        if (_embedder is not null && achievements.Count > 0)
        {
            try
            {
                var reqVec = await embed(responsibility);
                float bestScore = 0f;

                foreach (var ach in achievements)
                {
                    var achVec = await embed(ach.Text);
                    var score = _embedder.CosineSimilarity(reqVec, achVec);
                    if (score > bestScore) { bestScore = score; bestMatch = ach; }
                }

                if (bestScore >= _options.ResponsibilitySemanticThreshold)
                    return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, bestScore);
            }
            catch { /* fall through */ }
        }

        return new CoverageEntry(req, null, null, 0f);
    }
}
