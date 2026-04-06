using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace lucidRESUME.Core.Persistence;

public sealed class SqliteAppStore : IAppStore, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Vector store for embedding search. Shares the same connection and lock.</summary>
    public VectorStore Vectors { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SqliteAppStore(string dbPath, string? jsonMigrationPath = null)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        _conn.LoadVector();

        InitSchema();
        Vectors = new VectorStore(_conn, _lock);

        if (jsonMigrationPath is not null && File.Exists(jsonMigrationPath))
            MigrateFromJson(jsonMigrationPath);
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS app_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS resume (id INTEGER PRIMARY KEY CHECK (id = 1), data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS profile (id INTEGER PRIMARY KEY CHECK (id = 1), data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS saved_searches (id TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS search_presets (id TEXT PRIMARY KEY, data TEXT NOT NULL, is_custom INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS applications (id TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE VIRTUAL TABLE IF NOT EXISTS vec_embeddings USING vec0(embedding float[384]);
            CREATE TABLE IF NOT EXISTS vec_meta (
                rowid INTEGER PRIMARY KEY,
                source_type TEXT NOT NULL,
                source_id TEXT,
                text TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void MigrateFromJson(string jsonPath)
    {
        AppState state;
        using (var stream = File.OpenRead(jsonPath))
            state = JsonSerializer.Deserialize<AppState>(stream, JsonOpts) ?? new AppState();

        SaveCore(state);
        File.Move(jsonPath, jsonPath + ".bak", overwrite: true);
    }

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return LoadCore(); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { SaveCore(state); }
        finally { _lock.Release(); }
    }

    public async Task MutateAsync(Action<AppState> mutate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var state = LoadCore();
            mutate(state);
            SaveCore(state);
        }
        finally { _lock.Release(); }
    }

    public async Task ExportJsonAsync(Stream output, CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        await JsonSerializer.SerializeAsync(output, state, JsonOpts, ct);
    }

    public async Task ImportJsonAsync(Stream input, CancellationToken ct = default)
    {
        var state = await JsonSerializer.DeserializeAsync<AppState>(input, JsonOpts, ct)
            ?? new AppState();
        await SaveAsync(state, ct);
    }

    private AppState LoadCore()
    {
        var state = new AppState();

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM resume WHERE id = 1";
            if (cmd.ExecuteScalar() is string json)
                state.Resume = JsonSerializer.Deserialize<Models.Resume.ResumeDocument>(json, JsonOpts);
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM profile WHERE id = 1";
            if (cmd.ExecuteScalar() is string json)
                state.Profile = JsonSerializer.Deserialize<Models.Profile.UserProfile>(json, JsonOpts) ?? new();
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM jobs";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var job = JsonSerializer.Deserialize<Models.Jobs.JobDescription>(reader.GetString(0), JsonOpts);
                if (job is not null) state.Jobs.Add(job);
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM saved_searches";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var s = JsonSerializer.Deserialize<Models.Filters.SavedSearch>(reader.GetString(0), JsonOpts);
                if (s is not null) state.SavedSearches.Add(s);
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM search_presets WHERE is_custom = 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var p = JsonSerializer.Deserialize<Models.Filters.SearchPreset>(reader.GetString(0), JsonOpts);
                if (p is not null) state.CustomPresets.Add(p);
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM applications";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var app = JsonSerializer.Deserialize<Models.Tracking.JobApplication>(reader.GetString(0), JsonOpts);
                if (app is not null) state.Applications.Add(app);
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM app_meta WHERE key = 'last_saved'";
            if (cmd.ExecuteScalar() is string ts && DateTimeOffset.TryParse(ts, out var dt))
                state.LastSaved = dt;
        }

        return state;
    }

    private void SaveCore(AppState state)
    {
        state.LastSaved = DateTimeOffset.UtcNow;
        using var tx = _conn.BeginTransaction();

        // Resume
        if (state.Resume is not null)
            Upsert("resume", "id", "1", JsonSerializer.Serialize(state.Resume, JsonOpts));
        else
            Execute("DELETE FROM resume WHERE id = 1");

        // Profile
        Upsert("profile", "id", "1", JsonSerializer.Serialize(state.Profile, JsonOpts));

        // Jobs
        Execute("DELETE FROM jobs");
        foreach (var job in state.Jobs)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO jobs (id, data) VALUES ($id, $data)";
            cmd.Parameters.AddWithValue("$id", job.JobId.ToString());
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(job, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Saved searches
        Execute("DELETE FROM saved_searches");
        foreach (var s in state.SavedSearches)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO saved_searches (id, data) VALUES ($id, $data)";
            cmd.Parameters.AddWithValue("$id", s.SearchId);
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(s, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Custom presets only
        Execute("DELETE FROM search_presets WHERE is_custom = 1");
        foreach (var p in state.CustomPresets)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO search_presets (id, data, is_custom) VALUES ($id, $data, 1)";
            cmd.Parameters.AddWithValue("$id", p.PresetId);
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(p, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Applications
        Execute("DELETE FROM applications");
        foreach (var app in state.Applications)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO applications (id, data) VALUES ($id, $data)";
            cmd.Parameters.AddWithValue("$id", app.ApplicationId.ToString());
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(app, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        UpsertMeta("last_saved", state.LastSaved.ToString("O"));
        tx.Commit();
    }

    private void Upsert(string table, string keyCol, string keyVal, string data)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({keyCol}, data) VALUES ($key, $data) ON CONFLICT({keyCol}) DO UPDATE SET data = $data";
        cmd.Parameters.AddWithValue("$key", keyVal);
        cmd.Parameters.AddWithValue("$data", data);
        cmd.ExecuteNonQuery();
    }

    private void UpsertMeta(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO app_meta (key, value) VALUES ($key, $val) ON CONFLICT(key) DO UPDATE SET value = $val";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$val", value);
        cmd.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
        _lock.Dispose();
    }
}
