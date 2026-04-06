using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.Core.Persistence;

/// Single-user local store.
public interface IAppStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);

    /// <summary>
    /// Atomically loads, applies <paramref name="mutate"/>, and saves state
    /// under a single lock — prevents concurrent callers from overwriting each other.
    /// </summary>
    Task MutateAsync(Action<AppState> mutate, CancellationToken ct = default);

    /// <summary>Export full app state as JSON to a stream.</summary>
    Task ExportJsonAsync(Stream output, CancellationToken ct = default);

    /// <summary>Import app state from a JSON stream, replacing current state.</summary>
    Task ImportJsonAsync(Stream input, CancellationToken ct = default);
}

public sealed class AppState
{
    public ResumeDocument? Resume { get; set; }
    public UserProfile Profile { get; set; } = new();
    public List<JobDescription> Jobs { get; set; } = [];
    public DateTimeOffset LastSaved { get; set; }
    public List<SavedSearch> SavedSearches { get; set; } = [];
    public List<SearchPreset> CustomPresets { get; set; } = [];
    public List<JobApplication> Applications { get; set; } = [];
    public List<SearchWatch> SearchWatches { get; set; } = [];

    // Returns built-ins + custom presets merged
    public IReadOnlyList<SearchPreset> AllPresets =>
        [.. SearchPreset.BuiltIns, .. CustomPresets];
}
