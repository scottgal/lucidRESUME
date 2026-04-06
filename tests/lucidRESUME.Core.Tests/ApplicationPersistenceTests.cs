using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.Core.Tests;

public class ApplicationPersistenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAppStore _store;

    public ApplicationPersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lr_app_test_{Guid.NewGuid():N}.db");
        _store = new SqliteAppStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task SaveAndLoad_ApplicationsRoundTrip()
    {
        var app = new JobApplication
        {
            JobId = Guid.NewGuid(),
            CompanyName = "TestCorp",
            JobTitle = "Engineer",
        };
        app.AdvanceTo(ApplicationStage.Applied);
        app.AdvanceTo(ApplicationStage.Interview);
        app.AddNote("Great interview");
        app.Contact.RecruiterName = "Jane";
        app.Contact.RecruiterEmail = "jane@testcorp.com";

        await _store.MutateAsync(state =>
        {
            state.Applications.Add(app);
        });

        var loaded = await _store.LoadAsync();
        Assert.Single(loaded.Applications);

        var loadedApp = loaded.Applications[0];
        Assert.Equal("TestCorp", loadedApp.CompanyName);
        Assert.Equal("Engineer", loadedApp.JobTitle);
        Assert.Equal(ApplicationStage.Interview, loadedApp.Stage);
        Assert.Equal(3, loadedApp.Timeline.Count); // Applied + Interview + AddNote
        Assert.Equal("Jane", loadedApp.Contact.RecruiterName);
        Assert.Equal("jane@testcorp.com", loadedApp.Contact.RecruiterEmail);
        Assert.NotNull(loadedApp.AppliedAt);
    }

    [Fact]
    public async Task MutateAsync_UpdatesApplicationInPlace()
    {
        var appId = Guid.NewGuid();
        await _store.MutateAsync(state =>
        {
            state.Applications.Add(new JobApplication
            {
                ApplicationId = appId,
                JobId = Guid.NewGuid(),
                CompanyName = "Acme",
                JobTitle = "Dev"
            });
        });

        await _store.MutateAsync(state =>
        {
            var app = state.Applications.First(a => a.ApplicationId == appId);
            app.AdvanceTo(ApplicationStage.Applied);
        });

        var loaded = await _store.LoadAsync();
        Assert.Equal(ApplicationStage.Applied, loaded.Applications[0].Stage);
        Assert.Single(loaded.Applications[0].Timeline);
    }

    [Fact]
    public async Task EmptyApplications_LoadReturnsEmptyList()
    {
        var loaded = await _store.LoadAsync();
        Assert.Empty(loaded.Applications);
    }

    [Fact]
    public async Task MultipleApplications_AllPersisted()
    {
        await _store.MutateAsync(state =>
        {
            for (int i = 0; i < 5; i++)
            {
                state.Applications.Add(new JobApplication
                {
                    JobId = Guid.NewGuid(),
                    CompanyName = $"Company{i}",
                    JobTitle = $"Role{i}"
                });
            }
        });

        var loaded = await _store.LoadAsync();
        Assert.Equal(5, loaded.Applications.Count);
    }

    [Fact]
    public async Task ExportImportJson_PreservesApplications()
    {
        await _store.MutateAsync(state =>
        {
            var app = new JobApplication
            {
                JobId = Guid.NewGuid(),
                CompanyName = "ExportCo",
                JobTitle = "Exporter"
            };
            app.AdvanceTo(ApplicationStage.Offer);
            state.Applications.Add(app);
        });

        using var ms = new MemoryStream();
        await _store.ExportJsonAsync(ms);
        ms.Position = 0;

        // Import into a fresh store
        var dbPath2 = _dbPath + ".import.db";
        using var store2 = new SqliteAppStore(dbPath2);
        await store2.ImportJsonAsync(ms);

        var loaded = await store2.LoadAsync();
        Assert.Single(loaded.Applications);
        Assert.Equal("ExportCo", loaded.Applications[0].CompanyName);
        Assert.Equal(ApplicationStage.Offer, loaded.Applications[0].Stage);

        store2.Dispose();
        try { File.Delete(dbPath2); } catch { }
    }
}
