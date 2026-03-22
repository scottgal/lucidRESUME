using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using System.Text.Json;

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
        try { File.Delete(_dbPath); } catch { }
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
            Profile = new UserProfile { DisplayName = "Test User" },
            Jobs = [JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText })]
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded.Resume);
        Assert.Equal("cv.pdf", loaded.Resume!.FileName);
        Assert.Equal("Test User", loaded.Profile.DisplayName);
        Assert.Single(loaded.Jobs);
    }

    [Fact]
    public async Task MutateAsync_AtomicReadModifyWrite()
    {
        await _store.SaveAsync(new AppState { Profile = new UserProfile { DisplayName = "Before" } });

        await _store.MutateAsync(s => s.Profile.DisplayName = "After");

        var loaded = await _store.LoadAsync();
        Assert.Equal("After", loaded.Profile.DisplayName);
    }

    [Fact]
    public async Task ExportJsonAsync_ProducesValidJson()
    {
        var state = new AppState
        {
            Resume = ResumeDocument.Create("cv.pdf", "application/pdf", 1234),
            Profile = new UserProfile { DisplayName = "Export Test" }
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
            Profile = new UserProfile { DisplayName = "Imported" }
        };

        using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, original);
        ms.Position = 0;

        await _store.ImportJsonAsync(ms);

        var loaded = await _store.LoadAsync();
        Assert.Equal("Imported", loaded.Profile.DisplayName);
        Assert.NotNull(loaded.Resume);
        Assert.Equal("imported.pdf", loaded.Resume!.FileName);
    }

    [Fact]
    public async Task MigrateFromJson_ImportsAndRenames()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }

        var jsonPath = _dbPath + ".migration.json";
        var state = new AppState { Profile = new UserProfile { DisplayName = "Migrated" } };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(state));

        using var migratingStore = new SqliteAppStore(_dbPath, jsonMigrationPath: jsonPath);
        var loaded = await migratingStore.LoadAsync();

        Assert.Equal("Migrated", loaded.Profile.DisplayName);
        Assert.False(File.Exists(jsonPath), "Original JSON should be renamed");
        Assert.True(File.Exists(jsonPath + ".bak"), "Backup should exist");

        File.Delete(jsonPath + ".bak");
    }

    [Fact]
    public async Task FullCycle_SaveLoadMutateExport()
    {
        var state = new AppState();
        state.Resume = ResumeDocument.Create("test.pdf", "application/pdf", 100);
        state.Resume.Skills.Add(new Skill { Name = "C#" });
        state.Resume.Skills.Add(new Skill { Name = "SQL" });
        await _store.SaveAsync(state);

        var loaded = await _store.LoadAsync();
        Assert.Equal(2, loaded.Resume!.Skills.Count);

        await _store.MutateAsync(s => s.Resume!.Skills.Add(new Skill { Name = "Azure" }));
        loaded = await _store.LoadAsync();
        Assert.Equal(3, loaded.Resume!.Skills.Count);

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
        try { File.Delete(dbPath2); } catch { }
    }
}
