# Zero-Dependency lucidRESUME Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make lucidRESUME run fully offline with zero external services - SQLite+vec for persistence, ONNX embeddings locally, Docling optional.

**Architecture:** Replace `JsonAppStore` with `SqliteAppStore` backed by a single `data.db` file. Replace `OllamaEmbeddingService` with `OnnxEmbeddingService` using `all-MiniLM-L6-v2`. Make `IDoclingClient` an optional dependency in `ResumeParser` so the app works without Docling running.

**Tech Stack:** Microsoft.Data.Sqlite, sqlite-vec (NuGet: `SqliteVec`), Microsoft.ML.OnnxRuntime (already referenced), Microsoft.ML.Tokenizers, all-MiniLM-L6-v2 ONNX model.

---

### Task 1: Add NuGet packages for SQLite and ONNX tokenizer

**Files:**
- Modify: `src/lucidRESUME.Core/lucidRESUME.Core.csproj`
- Modify: `src/lucidRESUME.AI/lucidRESUME.AI.csproj`

**Step 1: Add SQLite packages to Core**

Core currently has zero dependencies. It needs SQLite for the new `SqliteAppStore`:

```xml
<!-- src/lucidRESUME.Core/lucidRESUME.Core.csproj -->
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.5" />
<PackageReference Include="SqliteVec" Version="0.1.6-alpha.2" />
```

**Step 2: Add tokenizer package to AI**

AI already has `Microsoft.Extensions.*`. Add the tokenizer for ONNX embedding:

```xml
<!-- src/lucidRESUME.AI/lucidRESUME.AI.csproj -->
<PackageReference Include="Microsoft.ML.Tokenizers" Version="1.0.3" />
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.24.3" />
```

**Step 3: Verify solution builds**

Run: `dotnet build lucidRESUME.sln`
Expected: 0 errors (warnings OK)

**Step 4: Commit**

```bash
git add src/lucidRESUME.Core/lucidRESUME.Core.csproj src/lucidRESUME.AI/lucidRESUME.AI.csproj
git commit -m "deps: add SQLite, sqlite-vec, ML.Tokenizers, OnnxRuntime packages"
```

---

### Task 2: Implement SqliteAppStore

**Files:**
- Create: `src/lucidRESUME.Core/Persistence/SqliteAppStore.cs`
- Test: `tests/lucidRESUME.Core.Tests/SqliteAppStoreTests.cs`

**Step 1: Write the failing tests**

```csharp
// tests/lucidRESUME.Core.Tests/SqliteAppStoreTests.cs
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.Core.Tests;

public class SqliteAppStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAppStore _store;

    public SqliteAppStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lucidresume_test_{Guid.NewGuid():N}.db");
        _store = new SqliteAppStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task LoadAsync_EmptyDb_ReturnsDefaultState()
    {
        var state = await _store.LoadAsync();
        Assert.NotNull(state);
        Assert.Null(state.Resume);
        Assert.NotNull(state.Profile);
        Assert.Empty(state.Jobs);
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_ResumeAndJobs()
    {
        var state = new AppState
        {
            Resume = ResumeDocument.Create("cv.pdf", "application/pdf", 1234),
            Profile = new UserProfile { FullName = "Test User" },
            Jobs = [JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText })]
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded.Resume);
        Assert.Equal("cv.pdf", loaded.Resume!.FileName);
        Assert.Equal("Test User", loaded.Profile.FullName);
        Assert.Single(loaded.Jobs);
    }

    [Fact]
    public async Task MutateAsync_AtomicReadModifyWrite()
    {
        await _store.SaveAsync(new AppState { Profile = new UserProfile { FullName = "Before" } });

        await _store.MutateAsync(s => s.Profile.FullName = "After");

        var loaded = await _store.LoadAsync();
        Assert.Equal("After", loaded.Profile.FullName);
    }

    [Fact]
    public async Task ExportJsonAsync_ProducesValidJson()
    {
        var state = new AppState
        {
            Resume = ResumeDocument.Create("cv.pdf", "application/pdf", 1234),
            Profile = new UserProfile { FullName = "Export Test" }
        };
        await _store.SaveAsync(state);

        using var ms = new MemoryStream();
        await _store.ExportJsonAsync(ms);
        ms.Position = 0;

        var json = new StreamReader(ms).ReadToEnd();
        Assert.Contains("Export Test", json);
        Assert.Contains("cv.pdf", json);
    }

    [Fact]
    public async Task ImportJsonAsync_RestoresState()
    {
        var original = new AppState
        {
            Resume = ResumeDocument.Create("imported.pdf", "application/pdf", 999),
            Profile = new UserProfile { FullName = "Imported" }
        };

        using var ms = new MemoryStream();
        await System.Text.Json.JsonSerializer.SerializeAsync(ms, original);
        ms.Position = 0;

        await _store.ImportJsonAsync(ms);

        var loaded = await _store.LoadAsync();
        Assert.Equal("Imported", loaded.Profile.FullName);
        Assert.NotNull(loaded.Resume);
        Assert.Equal("imported.pdf", loaded.Resume!.FileName);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~SqliteAppStoreTests" -v n`
Expected: FAIL - `SqliteAppStore` does not exist yet

**Step 3: Implement SqliteAppStore**

```csharp
// src/lucidRESUME.Core/Persistence/SqliteAppStore.cs
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace lucidRESUME.Core.Persistence;

public sealed class SqliteAppStore : IAppStore, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SqliteAppStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        // Load sqlite-vec extension
        SqliteVec.Sqlite.Load(_conn);

        InitSchema();
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

        // Resume
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM resume WHERE id = 1";
            if (cmd.ExecuteScalar() is string json)
                state.Resume = JsonSerializer.Deserialize<Models.Resume.ResumeDocument>(json, JsonOpts);
        }

        // Profile
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM profile WHERE id = 1";
            if (cmd.ExecuteScalar() is string json)
                state.Profile = JsonSerializer.Deserialize<Models.Profile.UserProfile>(json, JsonOpts) ?? new();
        }

        // Jobs
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

        // Saved searches
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

        // Custom presets
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

        // LastSaved from meta
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
        Upsert("resume", "id", "1", state.Resume is not null ? JsonSerializer.Serialize(state.Resume, JsonOpts) : null);

        // Profile
        Upsert("profile", "id", "1", JsonSerializer.Serialize(state.Profile, JsonOpts));

        // Jobs - delete-and-reinsert for simplicity (small dataset)
        Execute("DELETE FROM jobs");
        foreach (var job in state.Jobs)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO jobs (id, data) VALUES ($id, $data)";
            cmd.Parameters.AddWithValue("$id", job.Id ?? Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(job, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Saved searches
        Execute("DELETE FROM saved_searches");
        foreach (var s in state.SavedSearches)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO saved_searches (id, data) VALUES ($id, $data)";
            cmd.Parameters.AddWithValue("$id", s.Id ?? Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(s, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Custom presets
        Execute("DELETE FROM search_presets WHERE is_custom = 1");
        foreach (var p in state.CustomPresets)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO search_presets (id, data, is_custom) VALUES ($id, $data, 1)";
            cmd.Parameters.AddWithValue("$id", p.Id ?? Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(p, JsonOpts));
            cmd.ExecuteNonQuery();
        }

        // Meta
        UpsertMeta("last_saved", state.LastSaved.ToString("O"));

        tx.Commit();
    }

    private void Upsert(string table, string keyCol, string keyVal, string? data)
    {
        using var cmd = _conn.CreateCommand();
        if (data is null)
        {
            cmd.CommandText = $"DELETE FROM {table} WHERE {keyCol} = $key";
            cmd.Parameters.AddWithValue("$key", keyVal);
        }
        else
        {
            cmd.CommandText = $"INSERT INTO {table} ({keyCol}, data) VALUES ($key, $data) ON CONFLICT({keyCol}) DO UPDATE SET data = $data";
            cmd.Parameters.AddWithValue("$key", keyVal);
            cmd.Parameters.AddWithValue("$data", data);
        }
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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~SqliteAppStoreTests" -v n`
Expected: All 5 tests PASS

**Step 5: Commit**

```bash
git add src/lucidRESUME.Core/Persistence/SqliteAppStore.cs tests/lucidRESUME.Core.Tests/SqliteAppStoreTests.cs
git commit -m "feat: add SqliteAppStore with vec_embeddings table and JSON import/export"
```

---

### Task 3: Add first-launch JSON migration to SqliteAppStore

**Files:**
- Modify: `src/lucidRESUME.Core/Persistence/SqliteAppStore.cs`
- Test: `tests/lucidRESUME.Core.Tests/SqliteAppStoreTests.cs` (add test)

**Step 1: Write the failing test**

```csharp
// Add to SqliteAppStoreTests.cs
[Fact]
public async Task MigrateFromJson_ImportsAndRenames()
{
    // Create a data.json next to where data.db would be
    var dir = Path.GetDirectoryName(_dbPath)!;
    var jsonPath = Path.Combine(dir, $"lucidresume_test_{Path.GetFileNameWithoutExtension(_dbPath)}.json");
    var state = new AppState { Profile = new UserProfile { FullName = "Migrated" } };
    await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(state));

    // Dispose existing store, delete its db, create new one with migration
    _store.Dispose();
    File.Delete(_dbPath);

    using var migratingStore = new SqliteAppStore(_dbPath, jsonMigrationPath: jsonPath);
    var loaded = await migratingStore.LoadAsync();

    Assert.Equal("Migrated", loaded.Profile.FullName);
    Assert.False(File.Exists(jsonPath), "Original JSON should be renamed");
    Assert.True(File.Exists(jsonPath + ".bak"), "Backup should exist");

    // Cleanup
    File.Delete(jsonPath + ".bak");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~MigrateFromJson" -v n`
Expected: FAIL - constructor overload doesn't exist

**Step 3: Add migration constructor overload**

Add to `SqliteAppStore`:

```csharp
public SqliteAppStore(string dbPath, string? jsonMigrationPath = null) : this(dbPath)
{
    if (jsonMigrationPath is not null && File.Exists(jsonMigrationPath))
    {
        using var stream = File.OpenRead(jsonMigrationPath);
        var state = JsonSerializer.Deserialize<AppState>(stream, JsonOpts) ?? new AppState();
        SaveCore(state);
        File.Move(jsonMigrationPath, jsonMigrationPath + ".bak", overwrite: true);
    }
}
```

Note: Merge both constructors - the primary constructor does Open + InitSchema, then optionally migrates.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~SqliteAppStoreTests" -v n`
Expected: All 6 tests PASS

**Step 5: Commit**

```bash
git add src/lucidRESUME.Core/Persistence/SqliteAppStore.cs tests/lucidRESUME.Core.Tests/SqliteAppStoreTests.cs
git commit -m "feat: auto-migrate data.json to SQLite on first launch"
```

---

### Task 4: Download and bundle all-MiniLM-L6-v2 ONNX model

**Files:**
- Create: `models/README.md` (license attribution)
- Download: `models/all-MiniLM-L6-v2.onnx` and `models/tokenizer.json`
- Modify: `src/lucidRESUME.AI/lucidRESUME.AI.csproj` (content copy)

**Step 1: Download model files**

```bash
# From Hugging Face - the quantized ONNX export
mkdir -p models
curl -L "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx" -o models/all-MiniLM-L6-v2.onnx
curl -L "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/tokenizer.json" -o models/tokenizer.json
```

**Step 2: Add content items to AI csproj**

```xml
<!-- src/lucidRESUME.AI/lucidRESUME.AI.csproj -->
<ItemGroup>
  <Content Include="..\..\models\all-MiniLM-L6-v2.onnx" Link="models\all-MiniLM-L6-v2.onnx">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  <Content Include="..\..\models\tokenizer.json" Link="models\tokenizer.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Step 3: Verify build copies model to output**

Run: `dotnet build src/lucidRESUME.AI && ls src/lucidRESUME.AI/bin/Debug/net10.0/models/`
Expected: `all-MiniLM-L6-v2.onnx` and `tokenizer.json` present

**Step 4: Commit**

```bash
git add models/ src/lucidRESUME.AI/lucidRESUME.AI.csproj
git commit -m "assets: bundle all-MiniLM-L6-v2 ONNX model and tokenizer"
```

Note: The `.onnx` file is ~23MB. Consider adding to `.gitattributes` as LFS if the repo uses it.

---

### Task 5: Implement OnnxEmbeddingService

**Files:**
- Create: `src/lucidRESUME.AI/OnnxEmbeddingService.cs`
- Create: `src/lucidRESUME.AI/EmbeddingOptions.cs`
- Test: `tests/lucidRESUME.AI.Tests/OnnxEmbeddingServiceTests.cs`

**Step 1: Write the failing tests**

```csharp
// tests/lucidRESUME.AI.Tests/OnnxEmbeddingServiceTests.cs
using lucidRESUME.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public class OnnxEmbeddingServiceTests : IDisposable
{
    private readonly OnnxEmbeddingService _service;

    public OnnxEmbeddingServiceTests()
    {
        var opts = Options.Create(new EmbeddingOptions
        {
            OnnxModelPath = Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2.onnx"),
            TokenizerPath = Path.Combine(AppContext.BaseDirectory, "models", "tokenizer.json")
        });
        _service = new OnnxEmbeddingService(opts, NullLogger<OnnxEmbeddingService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task EmbedAsync_ReturnsCorrectDimensions()
    {
        var vec = await _service.EmbedAsync("software engineer");
        Assert.Equal(384, vec.Length);
    }

    [Fact]
    public async Task EmbedAsync_VectorIsNormalised()
    {
        var vec = await _service.EmbedAsync("hello world");
        float mag = 0;
        foreach (var v in vec) mag += v * v;
        Assert.InRange(MathF.Sqrt(mag), 0.99f, 1.01f);
    }

    [Fact]
    public async Task CosineSimilarity_SimilarTexts_HighScore()
    {
        var a = await _service.EmbedAsync("C# developer");
        var b = await _service.EmbedAsync(".NET software engineer");
        var sim = _service.CosineSimilarity(a, b);
        Assert.True(sim > 0.5f, $"Expected similar texts to have sim > 0.5 but got {sim}");
    }

    [Fact]
    public async Task CosineSimilarity_DifferentTexts_LowScore()
    {
        var a = await _service.EmbedAsync("C# developer");
        var b = await _service.EmbedAsync("banana smoothie recipe");
        var sim = _service.CosineSimilarity(a, b);
        Assert.True(sim < 0.3f, $"Expected different texts to have sim < 0.3 but got {sim}");
    }

    [Fact]
    public async Task EmbedAsync_CachesResults()
    {
        var a = await _service.EmbedAsync("test caching");
        var b = await _service.EmbedAsync("test caching");
        Assert.Same(a, b); // Same reference = cache hit
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/lucidRESUME.AI.Tests --filter "FullyQualifiedName~OnnxEmbeddingServiceTests" -v n`
Expected: FAIL - classes don't exist

**Step 3: Create EmbeddingOptions**

```csharp
// src/lucidRESUME.AI/EmbeddingOptions.cs
namespace lucidRESUME.AI;

public sealed class EmbeddingOptions
{
    /// <summary>"onnx" (default, local) or "ollama" (requires running Ollama service)</summary>
    public string Provider { get; set; } = "onnx";
    public string OnnxModelPath { get; set; } = "models/all-MiniLM-L6-v2.onnx";
    public string TokenizerPath { get; set; } = "models/tokenizer.json";
}
```

**Step 4: Implement OnnxEmbeddingService**

```csharp
// src/lucidRESUME.AI/OnnxEmbeddingService.cs
using System.Collections.Concurrent;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace lucidRESUME.AI;

public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly ConcurrentDictionary<string, float[]> _cache = new();
    private const int MaxCacheEntries = 500;
    private const int MaxSequenceLength = 256;

    public OnnxEmbeddingService(IOptions<EmbeddingOptions> options, ILogger<OnnxEmbeddingService> logger)
    {
        _logger = logger;
        var opts = options.Value;

        var modelPath = Path.IsPathRooted(opts.OnnxModelPath)
            ? opts.OnnxModelPath
            : Path.Combine(AppContext.BaseDirectory, opts.OnnxModelPath);
        var tokenizerPath = Path.IsPathRooted(opts.TokenizerPath)
            ? opts.TokenizerPath
            : Path.Combine(AppContext.BaseDirectory, opts.TokenizerPath);

        _session = new InferenceSession(modelPath);
        _tokenizer = Tokenizer.CreateFromFile(tokenizerPath);

        _logger.LogInformation("ONNX embedding model loaded from {Path} (384-dim)", modelPath);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(text, out var cached))
            return Task.FromResult(cached);

        var result = Embed(text);

        if (_cache.Count >= MaxCacheEntries)
        {
            // Evict ~10% of cache when full
            var keys = _cache.Keys.Take(MaxCacheEntries / 10).ToList();
            foreach (var k in keys) _cache.TryRemove(k, out _);
        }
        _cache[text] = result;

        return Task.FromResult(result);
    }

    private float[] Embed(string text)
    {
        var encoded = _tokenizer.Encode(text);
        var inputIds = encoded.Ids;
        var attentionMask = encoded.AttentionMask;

        // Truncate if needed
        var len = Math.Min(inputIds.Count, MaxSequenceLength);

        var inputIdsTensor = new DenseTensor<long>(new[] { 1, len });
        var attMaskTensor = new DenseTensor<long>(new[] { 1, len });
        var tokenTypeTensor = new DenseTensor<long>(new[] { 1, len });

        for (int i = 0; i < len; i++)
        {
            inputIdsTensor[0, i] = inputIds[i];
            attMaskTensor[0, i] = attentionMask[i];
            tokenTypeTensor[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeTensor)
        };

        using var results = _session.Run(inputs);

        // Output shape: [1, seq_len, 384] - mean pool over attention mask
        var output = results.First().AsEnumerable<float>().ToArray();
        var dims = 384;
        var pooled = new float[dims];

        float maskSum = 0;
        for (int i = 0; i < len; i++)
        {
            if (attMaskTensor[0, i] == 0) continue;
            maskSum++;
            for (int d = 0; d < dims; d++)
                pooled[d] += output[i * dims + d];
        }

        if (maskSum > 0)
            for (int d = 0; d < dims; d++)
                pooled[d] /= maskSum;

        Normalise(pooled);
        return pooled;
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private static void Normalise(float[] v)
    {
        float mag = 0f;
        foreach (var x in v) mag += x * x;
        mag = MathF.Sqrt(mag);
        if (mag < 1e-8f) return;
        for (int i = 0; i < v.Length; i++)
            v[i] /= mag;
    }

    public void Dispose() => _session.Dispose();
}
```

**Step 5: Add model content to test project csproj**

```xml
<!-- tests/lucidRESUME.AI.Tests/lucidRESUME.AI.Tests.csproj -->
<ItemGroup>
  <Content Include="..\..\models\all-MiniLM-L6-v2.onnx" Link="models\all-MiniLM-L6-v2.onnx">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  <Content Include="..\..\models\tokenizer.json" Link="models\tokenizer.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Step 6: Run tests to verify they pass**

Run: `dotnet test tests/lucidRESUME.AI.Tests --filter "FullyQualifiedName~OnnxEmbeddingServiceTests" -v n`
Expected: All 5 tests PASS

**Step 7: Commit**

```bash
git add src/lucidRESUME.AI/OnnxEmbeddingService.cs src/lucidRESUME.AI/EmbeddingOptions.cs tests/lucidRESUME.AI.Tests/
git commit -m "feat: add OnnxEmbeddingService using all-MiniLM-L6-v2 (384-dim, fully local)"
```

---

### Task 6: Make Docling optional in ResumeParser

**Files:**
- Modify: `src/lucidRESUME.Ingestion/Parsing/ResumeParser.cs`
- Modify: `src/lucidRESUME.Ingestion/Docling/DoclingOptions.cs` (add Enabled flag)
- Modify: `src/lucidRESUME.Ingestion/ServiceCollectionExtensions.cs`

**Step 1: Add Enabled flag to DoclingOptions**

```csharp
// Add to DoclingOptions.cs, first property:
public bool Enabled { get; set; } = false;
```

**Step 2: Make IDoclingClient optional in ResumeParser**

Change the constructor in `ResumeParser.cs`:

```csharp
// Change field from:
private readonly IDoclingClient _docling;
// To:
private readonly IDoclingClient? _docling;

// Change constructor parameter from:
public ResumeParser(
    IDoclingClient docling,
// To:
public ResumeParser(
    IDoclingClient? docling = null,
```

**Step 3: Update the Docling fallback in ParseAsync**

In `ResumeParser.ParseAsync`, change the `else` branch (around line 70):

```csharp
        else if (_docling is not null)
        {
            // ── 2. Docling fallback ───────────────────────────────────────
            _logger.LogInformation("Converting {File} via Docling", fileInfo.Name);
            var docling = await _docling.ConvertAsync(filePath, ct);
            resume.SetDoclingOutput(docling.Markdown, docling.Json, docling.PlainText);
            markdown = docling.Markdown;
            plainText = docling.PlainText;

            if (docling.PageImages.Count > 0)
            {
                var cacheKey = _imageCache.ComputeKey(filePath);
                resume.ImageCacheKey = cacheKey;
                resume.PageCount = docling.PageImages.Count;
                await _imageCache.StorePageAsync(cacheKey, 1, docling.PageImages[0], ct);
                if (docling.PageImages.Count > 1)
                    _ = CacheRemainingPagesAsync(cacheKey, docling.PageImages, ct);
            }
        }
        else
        {
            // ── 2b. No Docling, no direct parse - unsupported ─────────────
            var ext = fileInfo.Extension.ToLowerInvariant();
            throw new NotSupportedException(
                $"Cannot parse '{ext}' without Docling. Enable Docling in settings or use a PDF/DOCX file.");
        }
```

**Step 4: Update ServiceCollectionExtensions to conditionally register Docling**

```csharp
// src/lucidRESUME.Ingestion/ServiceCollectionExtensions.cs
public static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration config)
{
    var doclingSection = config.GetSection("Docling");
    services.Configure<DoclingOptions>(doclingSection);

    var doclingEnabled = doclingSection.GetValue<bool>("Enabled");
    if (doclingEnabled)
    {
        services.AddHttpClient<IDoclingClient, DoclingClient>()
            .AddStandardResilienceHandler();
    }

    services.AddSingleton<IDocumentImageCache>(_ => new FileSystemImageCache());
    services.AddDirectParsing();
    services.AddTransient<IResumeParser, ResumeParser>();
    return services;
}
```

**Step 5: Verify build and all existing tests still pass**

Run: `dotnet build lucidRESUME.sln && dotnet test`
Expected: 0 errors, all tests pass

**Step 6: Commit**

```bash
git add src/lucidRESUME.Ingestion/
git commit -m "feat: make Docling optional - direct parsers work without external services"
```

---

### Task 7: Add TxtParser for .txt files

**Files:**
- Create: `src/lucidRESUME.Parsing/TxtParser.cs`
- Modify: `src/lucidRESUME.Parsing/ServiceCollectionExtensions.cs`

**Step 1: Implement TxtParser**

```csharp
// src/lucidRESUME.Parsing/TxtParser.cs
namespace lucidRESUME.Parsing;

public sealed class TxtParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string> { ".txt" };

    public async Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(filePath, ct);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Simple heuristic: lines that are ALL CAPS or short bold-like lines → section headers
        var lines = text.Split('\n');
        var sections = new List<DocumentSection>();
        DocumentSection? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.Length > 0 && trimmed.Length <= 60 && trimmed == trimmed.ToUpperInvariant()
                && trimmed.Any(char.IsLetter))
            {
                current = new DocumentSection { Heading = trimmed, Level = 1 };
                sections.Add(current);
            }
            else if (current is not null)
            {
                current.Body += line + "\n";
            }
        }

        // Build markdown from sections, or just use raw text
        var markdown = sections.Count > 0
            ? string.Join("\n\n", sections.Select(s => $"## {s.Heading}\n\n{s.Body.Trim()}"))
            : text;

        return new ParsedDocument
        {
            Markdown = markdown,
            PlainText = text,
            Sections = sections,
            Confidence = sections.Count >= 2 ? 0.65 : 0.4,
            PageCount = 1
        };
    }
}
```

**Step 2: Register in DI**

In `src/lucidRESUME.Parsing/ServiceCollectionExtensions.cs`, add:

```csharp
services.AddSingleton<IDocumentParser, TxtParser>();
```

alongside the existing `PdfTextParser` and `DocxDirectParser` registrations.

**Step 3: Update SupportedExtensions in ResumeParser**

In `src/lucidRESUME.Ingestion/Parsing/ResumeParser.cs`, add `.txt` to the set:

```csharp
private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".pdf", ".docx", ".doc", ".txt"
};
```

(This was already added in the earlier SVG fix, so just verify `.txt` is present.)

**Step 4: Build and test**

Run: `dotnet build lucidRESUME.sln && dotnet test`
Expected: 0 errors, all tests pass

**Step 5: Commit**

```bash
git add src/lucidRESUME.Parsing/TxtParser.cs src/lucidRESUME.Parsing/ServiceCollectionExtensions.cs
git commit -m "feat: add TxtParser for plain text resume files"
```

---

### Task 8: Wire up new defaults in DI (App + CLI)

**Files:**
- Modify: `src/lucidRESUME/App.axaml.cs` (lines 193-222)
- Modify: `src/lucidRESUME.Cli/Infrastructure/ServiceBootstrap.cs`
- Modify: `src/lucidRESUME.AI/ServiceCollectionExtensions.cs`
- Modify: `src/lucidRESUME/appsettings.json`

**Step 1: Update AI ServiceCollectionExtensions**

```csharp
// src/lucidRESUME.AI/ServiceCollectionExtensions.cs
public static IServiceCollection AddAiTailoring(this IServiceCollection services, IConfiguration config)
{
    services.Configure<OllamaOptions>(config.GetSection("Ollama"));
    services.Configure<TailoringOptions>(config.GetSection("Tailoring"));
    services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));

    // Tailoring & extraction still use Ollama (optional - graceful failure)
    services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
        .AddStandardResilienceHandler();
    services.AddHttpClient<ILlmExtractionService, OllamaExtractionService>(client =>
        client.Timeout = TimeSpan.FromSeconds(60));

    // Embedding: ONNX by default, Ollama if configured
    var embeddingProvider = config.GetSection("Embedding").GetValue<string>("Provider") ?? "onnx";
    if (embeddingProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
    {
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>()
            .AddStandardResilienceHandler();
    }
    else
    {
        services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
    }

    return services;
}
```

**Step 2: Update App.axaml.cs - replace JsonAppStore with SqliteAppStore**

Change lines ~210-213 from:

```csharp
var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "lucidRESUME", "data.json");
services.AddSingleton<IAppStore>(_ => new JsonAppStore(appDataPath));
```

To:

```csharp
var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "lucidRESUME");
var dbPath = Path.Combine(appDataDir, "data.db");
var jsonPath = Path.Combine(appDataDir, "data.json");
services.AddSingleton<IAppStore>(_ => new SqliteAppStore(dbPath,
    jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null));
```

**Step 3: Update ServiceBootstrap.cs - add IAppStore for CLI**

The CLI currently doesn't register `IAppStore`. For commands that need it, add:

```csharp
var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "lucidRESUME");
var dbPath = Path.Combine(appDataDir, "data.db");
var jsonPath = Path.Combine(appDataDir, "data.json");
services.AddSingleton<IAppStore>(_ => new SqliteAppStore(dbPath,
    jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null));
```

**Step 4: Update appsettings.json**

Add Embedding section, set Docling.Enabled to false:

```json
{
  "Docling": {
    "Enabled": false,
    ...existing fields...
  },
  "Embedding": {
    "Provider": "onnx",
    "OnnxModelPath": "models/all-MiniLM-L6-v2.onnx",
    "TokenizerPath": "models/tokenizer.json"
  },
  ...rest unchanged...
}
```

**Step 5: Build and test everything**

Run: `dotnet build lucidRESUME.sln && dotnet test`
Expected: 0 errors, all tests pass

**Step 6: Commit**

```bash
git add src/lucidRESUME/App.axaml.cs src/lucidRESUME.Cli/Infrastructure/ServiceBootstrap.cs \
    src/lucidRESUME.AI/ServiceCollectionExtensions.cs src/lucidRESUME/appsettings.json
git commit -m "feat: wire SQLite + ONNX as defaults, Docling disabled by default"
```

---

### Task 9: Add IAppStore.ExportJsonAsync/ImportJsonAsync to interface

**Files:**
- Modify: `src/lucidRESUME.Core/Persistence/IAppStore.cs`
- Modify: `src/lucidRESUME.Core/Persistence/JsonAppStore.cs` (implement new methods)

**Step 1: Add methods to IAppStore**

```csharp
// Add to IAppStore interface:
/// <summary>Export full app state as JSON to a stream.</summary>
Task ExportJsonAsync(Stream output, CancellationToken ct = default);

/// <summary>Import app state from a JSON stream, replacing current state.</summary>
Task ImportJsonAsync(Stream input, CancellationToken ct = default);
```

**Step 2: Implement in JsonAppStore (for backwards compat / tests)**

```csharp
// Add to JsonAppStore:
public async Task ExportJsonAsync(Stream output, CancellationToken ct = default)
{
    var state = await LoadAsync(ct);
    await JsonSerializer.SerializeAsync(output, state, Options, ct);
}

public async Task ImportJsonAsync(Stream input, CancellationToken ct = default)
{
    var state = await JsonSerializer.DeserializeAsync<AppState>(input, Options, ct) ?? new AppState();
    await SaveAsync(state, ct);
}
```

**Step 3: Build and test**

Run: `dotnet build lucidRESUME.sln && dotnet test`
Expected: 0 errors, all tests pass

**Step 4: Commit**

```bash
git add src/lucidRESUME.Core/Persistence/IAppStore.cs src/lucidRESUME.Core/Persistence/JsonAppStore.cs
git commit -m "feat: add ExportJsonAsync/ImportJsonAsync to IAppStore interface"
```

---

### Task 10: Integration smoke test - full pipeline without external services

**Files:**
- Create: `tests/lucidRESUME.Core.Tests/ZeroDependencyIntegrationTests.cs`

**Step 1: Write integration test**

```csharp
// tests/lucidRESUME.Core.Tests/ZeroDependencyIntegrationTests.cs
using lucidRESUME.Core.Persistence;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Tests;

public class ZeroDependencyIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAppStore _store;

    public ZeroDependencyIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lucidresume_integ_{Guid.NewGuid():N}.db");
        _store = new SqliteAppStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task FullCycle_SaveLoadMutateExport()
    {
        // Save
        var state = new AppState();
        state.Resume = ResumeDocument.Create("test.pdf", "application/pdf", 100);
        state.Resume.Skills.Add(new Skill { Name = "C#" });
        state.Resume.Skills.Add(new Skill { Name = "SQL" });
        await _store.SaveAsync(state);

        // Load
        var loaded = await _store.LoadAsync();
        Assert.Equal(2, loaded.Resume!.Skills.Count);

        // Mutate
        await _store.MutateAsync(s => s.Resume!.Skills.Add(new Skill { Name = "Azure" }));
        loaded = await _store.LoadAsync();
        Assert.Equal(3, loaded.Resume!.Skills.Count);

        // Export
        using var ms = new MemoryStream();
        await _store.ExportJsonAsync(ms);
        Assert.True(ms.Length > 0);

        // Import into fresh store
        var dbPath2 = _dbPath + ".copy.db";
        using var store2 = new SqliteAppStore(dbPath2);
        ms.Position = 0;
        await store2.ImportJsonAsync(ms);
        var imported = await store2.LoadAsync();
        Assert.Equal(3, imported.Resume!.Skills.Count);

        store2.Dispose();
        File.Delete(dbPath2);
    }
}
```

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass including the new integration test

**Step 3: Commit**

```bash
git add tests/lucidRESUME.Core.Tests/ZeroDependencyIntegrationTests.cs
git commit -m "test: add zero-dependency integration test for SQLite store lifecycle"
```

---

### Task 11: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update architecture section**

Update the persistence description to reflect SQLite, and note that Docling/Ollama are optional. Add Embedding section to Configuration. Update the "Build & Run" section if any commands changed.

Key updates:
- "All user data stored in a single SQLite database at `%AppData%/lucidRESUME/data.db`"
- "JSON export/import available for backup and portability"
- "Auto-migrates from `data.json` on first launch"
- "Embeddings: local ONNX (all-MiniLM-L6-v2) by default, Ollama optional"
- "Docling: disabled by default, enable in appsettings.json for OCR support"

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for zero-dependency architecture"
```