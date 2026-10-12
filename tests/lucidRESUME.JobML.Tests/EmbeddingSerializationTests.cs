using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class EmbeddingSerializationTests
{
    [Fact]
    public void Embeddings_use_compact_yaml_sequences_without_losing_float_values()
    {
        var parser = new JobMlParser();
        var values = new List<float> { 0, 1, -0.123456789f, 1.2e-20f, float.MaxValue };
        var root = new JobMlRoot
        {
            Concepts = [new JobMlConcept { Id = "python", Name = "Python",
            Embedding = new JobMlEmbedding { Space = "test", Vector = values } }]
        };
        var yaml = parser.SerializeYaml(root);
        Assert.Contains("vector: [", yaml);
        Assert.Equal(values, Assert.Single(parser.ParseYaml(yaml).Concepts).Embedding!.Vector);
    }
}
