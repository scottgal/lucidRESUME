using System.Text.Json;

namespace lucidRESUME.Core.Persistence;

public sealed class JsonAppStore : IAppStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonAppStore(string filePath) => _filePath = filePath;

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return new AppState();

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppState>(stream, Options, ct) ?? new AppState();
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        state.LastSaved = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, Options, ct);
    }
}
