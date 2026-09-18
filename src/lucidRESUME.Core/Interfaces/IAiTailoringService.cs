using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface IAiTailoringService
{
    bool IsAvailable { get; }
    Task<bool> CheckAvailabilityAsync(CancellationToken ct = default) => Task.FromResult(IsAvailable);
    Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job, UserProfile profile, CancellationToken ct = default);
}
