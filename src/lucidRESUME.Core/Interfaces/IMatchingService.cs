using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public record MatchResult(double Score, List<string> MatchedSkills, List<string> MissingSkills, string Summary);

public interface IMatchingService
{
    Task<MatchResult> MatchAsync(ResumeDocument resume, JobDescription job, UserProfile profile, CancellationToken ct = default);
}
