using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Persistence;

/// Single-user local store — all data lives in one JSON file.
public interface IAppStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);
}

public sealed class AppState
{
    public ResumeDocument? Resume { get; set; }
    public UserProfile Profile { get; set; } = new();
    public List<JobDescription> Jobs { get; set; } = [];
    public DateTimeOffset LastSaved { get; set; }
}
