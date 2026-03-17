using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed class ResumeCoverageAnalyser : ICoverageAnalyser
{
    private readonly CompanyClassifier _classifier;
    private readonly IEmbeddingService? _embedder;

    public ResumeCoverageAnalyser(CompanyClassifier classifier,
        IEmbeddingService? embedder = null)
    {
        _classifier = classifier;
        _embedder = embedder;
    }

    public async Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default)
    {
        var companyType = _classifier.Classify(job);

        var skillEvidence = resume.Skills
            .Select((s, i) => (Text: s.Name, Section: $"Skills[{i}]"))
            .ToList();

        var techEvidence = resume.Experience
            .SelectMany((e, ei) => e.Technologies.Select((t, ti) =>
                (Text: t, Section: $"Experience[{ei}].Technologies[{ti}]")))
            .ToList();

        var achievementEvidence = resume.Experience
            .SelectMany((e, ei) => e.Achievements.Select((a, ai) =>
                (Text: a, Section: $"Experience[{ei}].Achievements[{ai}]")))
            .ToList();

        var allSkillEvidence = skillEvidence.Concat(techEvidence).ToList();

        var entries = new List<CoverageEntry>();

        foreach (var req in job.RequiredSkills)
            entries.Add(await MatchSkillAsync(req, RequirementPriority.Required, allSkillEvidence, ct));

        foreach (var pref in job.PreferredSkills)
            entries.Add(await MatchSkillAsync(pref, RequirementPriority.Preferred, allSkillEvidence, ct));

        foreach (var resp in job.Responsibilities)
            entries.Add(await MatchResponsibilityAsync(resp, achievementEvidence, ct));

        return new CoverageReport(entries.AsReadOnly(), companyType, DateTimeOffset.UtcNow);
    }

    private async Task<CoverageEntry> MatchSkillAsync(
        string requirement,
        RequirementPriority priority,
        IReadOnlyList<(string Text, string Section)> evidence,
        CancellationToken ct)
    {
        var req = new JdRequirement(requirement, priority);

        // 1. Exact (case-insensitive) match
        var exact = evidence.FirstOrDefault(e =>
            e.Text.Contains(requirement, StringComparison.OrdinalIgnoreCase));
        if (exact != default)
            return new CoverageEntry(req, exact.Text, exact.Section, 1.0f);

        // 2. Semantic match if embedder available
        if (_embedder is not null)
        {
            try
            {
                var reqVec = await _embedder.EmbedAsync(requirement, ct);
                float bestScore = 0f;
                (string Text, string Section) bestMatch = default;

                foreach (var e in evidence)
                {
                    var eVec = await _embedder.EmbedAsync(e.Text, ct);
                    var score = _embedder.CosineSimilarity(reqVec, eVec);
                    if (score > bestScore) { bestScore = score; bestMatch = e; }
                }

                if (bestScore >= 0.82f)
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
        CancellationToken ct)
    {
        var req = new JdRequirement(responsibility, RequirementPriority.Responsibility);

        var keywords = responsibility.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', '(', ')').ToLowerInvariant())
            .Where(w => w.Length > 4 && !StopWords.Contains(w))
            .ToHashSet();

        (string Text, string Section) bestMatch = default;
        int bestOverlap = 0;

        foreach (var ach in achievements)
        {
            var achLower = ach.Text.ToLowerInvariant();
            int overlap = keywords.Count(k =>
                achLower.Contains(k) ||
                (k.EndsWith('s') && k.Length > 4 && achLower.Contains(k[..^1])));
            if (overlap > bestOverlap) { bestOverlap = overlap; bestMatch = ach; }
        }

        if (bestOverlap >= 2)
        {
            var score = Math.Min(1f, bestOverlap / (float)Math.Max(keywords.Count, 1));
            return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, score);
        }

        // Semantic fallback
        if (_embedder is not null && achievements.Count > 0)
        {
            try
            {
                var reqVec = await _embedder.EmbedAsync(responsibility, ct);
                float bestScore = 0f;

                foreach (var ach in achievements)
                {
                    var achVec = await _embedder.EmbedAsync(ach.Text, ct);
                    var score = _embedder.CosineSimilarity(reqVec, achVec);
                    if (score > bestScore) { bestScore = score; bestMatch = ach; }
                }

                if (bestScore >= 0.75f)
                    return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, bestScore);
            }
            catch { /* fall through */ }
        }

        return new CoverageEntry(req, null, null, 0f);
    }

    private static readonly HashSet<string> StopWords =
    [
        "about", "above", "after", "also", "among", "being", "between",
        "their", "there", "these", "those", "through", "using", "where",
        "which", "while", "will", "with", "working", "within", "would",
        "experience", "ability", "knowledge", "skills", "strong", "across"
    ];
}
