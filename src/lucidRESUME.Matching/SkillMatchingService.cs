using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed class SkillMatchingService : IMatchingService
{
    private readonly AspectExtractor _extractor;
    private readonly IEmbeddingService? _embedder;
    private const float SemanticThreshold = 0.82f;

    public SkillMatchingService(AspectExtractor extractor, IEmbeddingService? embeddingService = null)
    {
        _extractor = extractor;
        _embedder  = embeddingService;
    }

    public async Task<MatchResult> MatchAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        // Blocked company gate
        if (job.Company != null && profile.BlockedCompanies
            .Any(b => job.Company.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            return new MatchResult(0, [], [], $"Company '{job.Company}' is blocked.");
        }

        var resumeSkills = resume.Skills.Select(s => s.Name).ToList();
        var avoidNames   = profile.SkillsToAvoid.Select(s => s.SkillName)
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required     = job.RequiredSkills;

        List<string> matched;
        List<string> missing;

        if (_embedder is not null && required.Count > 0 && resumeSkills.Count > 0)
        {
            (matched, missing) = await SemanticMatchAsync(required, resumeSkills, _embedder, ct);
        }
        else
        {
            var resumeSet = resumeSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
            matched = required.Where(s => resumeSet.Contains(s)).ToList();
            missing = required.Where(s => !resumeSet.Contains(s)).ToList();
        }

        var avoidHits = required.Where(s => avoidNames.Contains(s)).ToList();

        double baseScore = required.Count == 0 ? 0.5 : (double)matched.Count / required.Count;
        baseScore -= avoidHits.Count * 0.1;
        baseScore = Math.Clamp(baseScore, 0, 1);

        // ── Vote-weighted adjustment ─────────────────────────────────────────
        var aspects        = _extractor.Extract(job);
        double voteAdjust  = aspects.Sum(a => profile.GetVoteScore(a.Type, a.Value) * 0.05);
        double finalScore  = Math.Clamp(baseScore + voteAdjust, 0.0, 1.0);

        var summary = $"{matched.Count}/{required.Count} required skills matched. Score: {finalScore:P0}.";
        if (avoidHits.Count > 0)
            summary += $" Note: {avoidHits.Count} skill(s) you prefer to avoid are required.";

        var topVoted = aspects
            .Select(a => (Aspect: a, Score: profile.GetVoteScore(a.Type, a.Value)))
            .Where(x => x.Score != 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        if (topVoted.Count > 0)
        {
            var aspectSummary = string.Join(", ", topVoted.Select(x =>
                $"{x.Aspect.Value} ({(x.Score > 0 ? "+" : "")}{x.Score})"));
            summary += $" Top aspects: {aspectSummary}.";
        }

        return new MatchResult(finalScore, matched, missing, summary);
    }

    private async Task<(List<string> matched, List<string> missing)> SemanticMatchAsync(
        IReadOnlyList<string> required,
        IReadOnlyList<string> resumeSkills,
        IEmbeddingService embedder,
        CancellationToken ct)
    {
        float[][]? reqVecs;
        float[][]? resumeVecs;

        try
        {
            reqVecs    = await Task.WhenAll(required.Select(s => embedder.EmbedAsync(s, ct)));
            resumeVecs = await Task.WhenAll(resumeSkills.Select(s => embedder.EmbedAsync(s, ct)));
        }
        catch
        {
            // Embedding failed — fall back to exact match
            var set      = resumeSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matched2 = required.Where(s => set.Contains(s)).ToList();
            var missing2 = required.Where(s => !set.Contains(s)).ToList();
            return (matched2, missing2);
        }

        var matched = new List<string>();
        var missing = new List<string>();

        for (int i = 0; i < required.Count; i++)
        {
            float bestSim = resumeVecs.Length == 0
                ? 0f
                : resumeVecs.Max(rv => embedder.CosineSimilarity(reqVecs[i], rv));

            if (bestSim >= SemanticThreshold)
                matched.Add(required[i]);
            else
                missing.Add(required[i]);
        }

        return (matched, missing);
    }
}
