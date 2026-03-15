using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Persistence;

/// Single-user local store — all data lives in one JSON file.
public interface IAppStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);

    /// <summary>
    /// Atomically loads, applies <paramref name="mutate"/>, and saves state
    /// under a single lock — prevents concurrent callers from overwriting each other.
    /// </summary>
    Task MutateAsync(Action<AppState> mutate, CancellationToken ct = default);
}

public sealed class AppState
{
    public ResumeDocument? Resume { get; set; }
    public UserProfile Profile { get; set; } = new();
    public List<JobDescription> Jobs { get; set; } = [];
    public DateTimeOffset LastSaved { get; set; }
    public List<SavedSearch> SavedSearches { get; set; } = [];
    public List<SearchPreset> CustomPresets { get; set; } = [];

    // Returns built-ins + custom presets merged
    public IReadOnlyList<SearchPreset> AllPresets =>
        [.. SearchPreset.BuiltIns, .. CustomPresets];
}
