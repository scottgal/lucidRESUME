using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public sealed class SkillTaxonomyServiceTests
{
    [Fact]
    public void ExactMatchingDoesNotEmitEveryNestedTaxonomyAlias()
    {
        var taxonomy = new SkillTaxonomyService(new NoOpEmbedder());
        var matches = taxonomy.FindSkillsExact("Cloud architecture, C#, ASP.NET Core, CI/CD and Kubernetes.");

        Assert.Contains(matches, x => x.Equals("Cloud architecture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(matches, x => x.Equals("C#", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(matches, x => x.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase));
        Assert.True(matches.Count <= 6, "Nested aliases produced: " + string.Join(", ", matches));
    }

    private sealed class NoOpEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
