using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed class SkillMatchingService : IMatchingService
{
    private readonly AspectExtractor _extractor;

    public SkillMatchingService(AspectExtractor extractor)
    {
        _extractor = extractor;
    }

    public Task<MatchResult> MatchAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        // Check blocked companies first
        if (job.Company != null && profile.BlockedCompanies
            .Any(b => job.Company.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new MatchResult(0, [], [], $"Company '{job.Company}' is blocked (on your blocklist)."));
        }

        var resumeSkillNames = resume.Skills.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var avoidSkillNames = profile.SkillsToAvoid.Select(s => s.SkillName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var required = job.RequiredSkills;
        var matched = required.Where(s => resumeSkillNames.Contains(s)).ToList();
        var missing = required.Where(s => !resumeSkillNames.Contains(s)).ToList();
        var avoidHits = required.Where(s => avoidSkillNames.Contains(s)).ToList();

        // ── Base score ───────────────────────────────────────────────────────
        double baseScore = required.Count == 0 ? 0.5 : (double)matched.Count / required.Count;
        baseScore -= avoidHits.Count * 0.1;
        baseScore = Math.Clamp(baseScore, 0, 1);

        // ── Vote-weighted adjustment ─────────────────────────────────────────
        var aspects = _extractor.Extract(job);
        double voteAdjustment = aspects.Sum(a => profile.GetVoteScore(a.Type, a.Value) * 0.05);
        double finalScore = Math.Clamp(baseScore + voteAdjustment, 0.0, 1.0);

        // ── Summary ──────────────────────────────────────────────────────────
        var summary = $"{matched.Count}/{required.Count} required skills matched. Score: {finalScore:P0}.";
        if (avoidHits.Count > 0)
            summary += $" Note: {avoidHits.Count} skill(s) you prefer to avoid are required.";

        // Include top voted aspects (score != 0) in summary
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

        return Task.FromResult(new MatchResult(finalScore, matched, missing, summary));
    }
}
