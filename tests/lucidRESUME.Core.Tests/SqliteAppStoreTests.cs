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
        Assert.Null(state.SelectedResume);
        Assert.Empty(state.Resumes);
        Assert.NotNull(state.Profile);
        Assert.Empty(state.Jobs);
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_ResumeAndJobs()
    {
        var state = new AppState
        {
            Profile = new UserProfile { DisplayName = "Test User" },
            Jobs = [JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText })]
        };
        state.AddOrReplaceResume(ResumeDocument.Create("cv.pdf", "application/pdf", 1234));

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded.SelectedResume);
        Assert.Equal("cv.pdf", loaded.SelectedResume!.FileName);
        Assert.Single(loaded.Resumes);
        Assert.Equal("Test User", loaded.Profile.DisplayName);
        Assert.Single(loaded.Jobs);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsMultipleResumesAndSelection()
    {
        var first = ResumeDocument.Create("general.pdf", "application/pdf", 100);
        first.Skills.Add(new Skill { Name = "C#" });
        var second = ResumeDocument.Create("old.pdf", "application/pdf", 200);
        second.Skills.Add(new Skill { Name = "Perl" });

        var state = new AppState();
        state.AddOrReplaceResume(first);
        state.AddOrReplaceResume(second);

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.Equal(2, loaded.Resumes.Count);
        Assert.Equal(second.ResumeId, loaded.SelectedResumeId);
        Assert.Equal("old.pdf", loaded.SelectedResume!.FileName);

        var aggregate = loaded.BuildAggregateResume();
        Assert.NotNull(aggregate);
        Assert.Contains(aggregate!.Skills, s => s.Name == "C#");
        Assert.Contains(aggregate.Skills, s => s.Name == "Perl");
    }

    [Fact]
    public async Task SaveAsync_RoundTripsOverridesAndEmployerProfile()
    {
        var state = new AppState
        {
            Overrides = new UserOverrides
            {
                DismissedSkills = ["COBOL"],
                PersonalInfoOverrides = new Dictionary<string, string> { ["FullName"] = "Correct Name" },
                ManualSkills = [new ManualSkillEntry { SkillName = "Rust", Category = "Languages" }]
            },
            EmployerProfile = new EmployerProfile
            {
                CompanyName = "Example Ltd",
                Industry = "Software",
                CompanySize = CompanySizeRange.Medium
            }
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.Contains("COBOL", loaded.Overrides.DismissedSkills);
        Assert.Equal("Correct Name", loaded.Overrides.PersonalInfoOverrides["FullName"]);
        Assert.Equal("Rust", Assert.Single(loaded.Overrides.ManualSkills).SkillName);
        Assert.Equal("Example Ltd", loaded.EmployerProfile?.CompanyName);
        Assert.Equal(CompanySizeRange.Medium, loaded.EmployerProfile?.CompanySize);
    }

    [Fact]
    public async Task ImportJsonAsync_RoundTripsOverridesAndEmployerProfile()
    {
        var original = new AppState
        {
            Overrides = new UserOverrides { DismissedSkills = ["Perl"] },
            EmployerProfile = new EmployerProfile { CompanyName = "Import Ltd" }
        };
        using var input = new MemoryStream();
        await JsonSerializer.SerializeAsync(input, original);
        input.Position = 0;

        await _store.ImportJsonAsync(input);
        var loaded = await _store.LoadAsync();

        Assert.Contains("Perl", loaded.Overrides.DismissedSkills);
        Assert.Equal("Import Ltd", loaded.EmployerProfile?.CompanyName);
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
            Profile = new UserProfile { DisplayName = "Export Test" }
        };
        state.AddOrReplaceResume(ResumeDocument.Create("cv.pdf", "application/pdf", 1234));
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
            Profile = new UserProfile { DisplayName = "Imported" }
        };
        original.AddOrReplaceResume(ResumeDocument.Create("imported.pdf", "application/pdf", 999));

        using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, original);
        ms.Position = 0;

        await _store.ImportJsonAsync(ms);

        var loaded = await _store.LoadAsync();
        Assert.Equal("Imported", loaded.Profile.DisplayName);
        Assert.NotNull(loaded.SelectedResume);
        Assert.Equal("imported.pdf", loaded.SelectedResume!.FileName);
    }

    [Fact]
    public async Task MigrateFromJson_ImportsAndRenames()
    {
        var migrationDbPath = Path.Combine(Path.GetTempPath(), $"lucidresume_migration_{Guid.NewGuid():N}.db");
        var jsonPath = migrationDbPath + ".migration.json";
        var state = new AppState { Profile = new UserProfile { DisplayName = "Migrated" } };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(state));

        using var migratingStore = new SqliteAppStore(migrationDbPath, jsonMigrationPath: jsonPath);
        var loaded = await migratingStore.LoadAsync();

        Assert.Equal("Migrated", loaded.Profile.DisplayName);
        Assert.False(File.Exists(jsonPath), "Original JSON should be renamed");
        Assert.True(File.Exists(jsonPath + ".bak"), "Backup should exist");

        File.Delete(jsonPath + ".bak");
        try { File.Delete(migrationDbPath); } catch { }
    }

    [Fact]
    public async Task FullCycle_SaveLoadMutateExport()
    {
        var state = new AppState();
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 100);
        resume.Skills.Add(new Skill { Name = "C#" });
        resume.Skills.Add(new Skill { Name = "SQL" });
        state.AddOrReplaceResume(resume);
        await _store.SaveAsync(state);

        var loaded = await _store.LoadAsync();
        Assert.Equal(2, loaded.SelectedResume!.Skills.Count);

        await _store.MutateAsync(s => s.SelectedResume!.Skills.Add(new Skill { Name = "Azure" }));
        loaded = await _store.LoadAsync();
        Assert.Equal(3, loaded.SelectedResume!.Skills.Count);

        using var ms = new MemoryStream();
        await _store.ExportJsonAsync(ms);
        Assert.True(ms.Length > 0);

        // Import into fresh store
        var dbPath2 = _dbPath + ".copy.db";
        using var store2 = new SqliteAppStore(dbPath2);
        ms.Position = 0;
        await store2.ImportJsonAsync(ms);
        var imported = await store2.LoadAsync();
        Assert.Equal(3, imported.SelectedResume!.Skills.Count);

        store2.Dispose();
        try { File.Delete(dbPath2); } catch { }
    }
}
