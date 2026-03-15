using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.Core.Tests.Persistence;

public class JsonAppStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"lucidresume_test_{Guid.NewGuid()}.json");

    [Fact]
    public async Task SaveAndLoad_RoundTripsAppState()
    {
        var store = new JsonAppStore(_tempPath);
        var state = new AppState
        {
            Profile = new UserProfile { DisplayName = "Test User" },
            Jobs = [JobDescription.Create("Senior Dev role", new JobSource { Type = JobSourceType.PastedText })]
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.Equal("Test User", loaded.Profile.DisplayName);
        Assert.Single(loaded.Jobs);
    }

    [Fact]
    public async Task Load_WhenNoFile_ReturnsEmptyState()
    {
        var store = new JsonAppStore(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json"));
        var state = await store.LoadAsync();
        Assert.NotNull(state);
        Assert.Empty(state.Jobs);
    }

    public void Dispose() => File.Delete(_tempPath);
}
