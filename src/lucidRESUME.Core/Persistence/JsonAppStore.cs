using System.Text.Json;

namespace lucidRESUME.Core.Persistence;

public sealed class JsonAppStore : IAppStore
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

    private async Task<AppState> LoadCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new AppState();

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppState>(stream, Options, ct) ?? new AppState();
    }

    private async Task SaveCoreAsync(AppState state, CancellationToken ct)
    {
        state.LastSaved = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        // Write to a temp file then rename for atomicity — prevents truncated JSON on crash
        var tmp = Path.Combine(dir, Path.GetRandomFileName());
        try
        {
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, state, Options, ct);

            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
