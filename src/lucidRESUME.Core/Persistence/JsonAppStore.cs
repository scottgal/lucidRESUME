using System.Text.Json;

namespace lucidRESUME.Core.Persistence;

public sealed class JsonAppStore : IAppStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonAppStore(string filePath) => _filePath = filePath;

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _lock.WaitAsync(ct);
        try
        {
            await SaveCoreAsync(state, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Atomically loads, mutates, and saves state under a single lock acquisition,
    /// preventing concurrent callers from reading stale state and overwriting each other's writes.
    /// </summary>
    public async Task MutateAsync(Action<AppState> mutate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _lock.WaitAsync(ct);
        try
        {
            var state = await LoadCoreAsync(ct);
            mutate(state);
            await SaveCoreAsync(state, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExportJsonAsync(Stream output, CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        await JsonSerializer.SerializeAsync(output, state, Options, ct);
    }

    public async Task ImportJsonAsync(Stream input, CancellationToken ct = default)
    {
        var state = await JsonSerializer.DeserializeAsync<AppState>(input, Options, ct) ?? new AppState();
        state.NormalizeResumes();
        await SaveAsync(state, ct);
    }

    private async Task<AppState> LoadCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new AppState();

        using var stream = File.OpenRead(_filePath);
        var state = await JsonSerializer.DeserializeAsync<AppState>(stream, Options, ct) ?? new AppState();
        state.NormalizeResumes();
        if (state.CompleteCareer is null && state.Resumes.Count > 0) await SaveCoreAsync(state, ct);
        return state;
    }

    private async Task SaveCoreAsync(AppState state, CancellationToken ct)
    {
        state.ReconcileCompleteTranscript();
        state.NormalizeResumes();
        if (state.CompleteCareer is { } career) Models.Resume.CareerProseLexicalIndex.Build(career.Transcript);
        state.LastSaved = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        // Write to a temp file then rename for atomicity - prevents truncated JSON on crash
        var tmp = Path.Combine(dir, Path.GetRandomFileName());
        try
        {
            using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, state, Options, ct);

            File.Move(tmp, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; preserve the original serialization failure.
            }
        }
    }

    public void Dispose() => _lock.Dispose();
}
