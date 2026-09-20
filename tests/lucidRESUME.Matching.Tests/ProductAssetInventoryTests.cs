using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public sealed class ProductAssetInventoryTests
{
    [Fact]
    public void BuildOutputContainsEveryRequiredProductAsset()
    {
        var audit = ProductAssetInventory.Audit(AppContext.BaseDirectory);
        Assert.True(audit.IsComplete, "Missing product assets: " + string.Join(", ", audit.Missing));
        Assert.DoesNotContain(audit.Resolved.Keys, x =>
            x.Contains("resume", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("ledger", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("data.db", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LeadershipProfilesShipAndProduceCentroids()
    {
        var taxonomy = new SkillTaxonomyService(new DeterministicEmbedder());
        Assert.Contains("Lead Developer", taxonomy.Roles);
        Assert.Contains("Head of Engineering", taxonomy.Roles);
        Assert.Contains("CTO", taxonomy.Roles);
        Assert.Contains("Engineering leadership", taxonomy.GetRoleSkills("CTO"));
        Assert.True(taxonomy.Diagnostics.UniqueSkills > 10_000);
        Assert.Equal(2, taxonomy.Diagnostics.SourcePaths.Count);

        var centroid = await taxonomy.GetRoleCentroidAsync("CTO");
        Assert.Equal(8, centroid.Length);
        Assert.InRange(Math.Sqrt(centroid.Sum(x => x * x)), .999, 1.001);
    }

    private sealed class DeterministicEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var result = new float[8];
            foreach (var c in text) result[c % result.Length] += 1;
            return Task.FromResult(result);
        }

        public float CosineSimilarity(float[] a, float[] b) => a.Zip(b).Sum(x => x.First * x.Second);
    }
}
