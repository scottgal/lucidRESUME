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
            VocabPath = Path.Combine(AppContext.BaseDirectory, "models", "vocab.txt")
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
        Assert.True(sim > 0.4f, $"Expected similar texts to have sim > 0.4 but got {sim}");
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
        Assert.Same(a, b);
    }
}
