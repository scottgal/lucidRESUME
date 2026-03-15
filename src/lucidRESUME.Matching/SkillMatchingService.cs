using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed class SkillMatchingService : IMatchingService
{
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

        double score = required.Count == 0 ? 0.5 : (double)matched.Count / required.Count;
        score -= avoidHits.Count * 0.1;
        score = Math.Clamp(score, 0, 1);

        var summary = $"{matched.Count}/{required.Count} required skills matched. Score: {score:P0}.";
        if (avoidHits.Count > 0)
            summary += $" Note: {avoidHits.Count} skill(s) you prefer to avoid are required.";

        return Task.FromResult(new MatchResult(score, matched, missing, summary));
    }
}
