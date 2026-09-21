using lucidRESUME.Services;

namespace lucidRESUME.App.Tests;

public sealed class PlatformSecretStoreTests
{
    [Fact]
    public async Task RoundTripsThroughAvailableOperatingSystemStore()
    {
        var store = new PlatformSecretStore();
        if (!store.IsAvailable) return;

        var name = $"integration-test-{Guid.NewGuid():N}";
        const string secret = "dummy-secret-not-a-real-api-key";
        try
        {
            await store.SetAsync(name, secret);
            Assert.Equal(secret, await store.GetAsync(name));
        }
        finally
        {
            await store.DeleteAsync(name);
        }

        Assert.Null(await store.GetAsync(name));
    }

    [Fact]
    public async Task EmptyValueDeletesCredential()
    {
        var store = new PlatformSecretStore();
        if (!store.IsAvailable) return;

        var name = $"integration-test-{Guid.NewGuid():N}";
        await store.SetAsync(name, "temporary");
        await store.SetAsync(name, "");
        Assert.Null(await store.GetAsync(name));
    }
}
