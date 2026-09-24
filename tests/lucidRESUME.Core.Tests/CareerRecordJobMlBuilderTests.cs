using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Core.Tests;

public sealed class CareerRecordJobMlBuilderTests
{
    [Fact]
    public async Task Career_record_round_trips_with_sources_vectors_and_role_centroids()
    {
        var resume = ResumeDocument.Create("source.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 123);
        resume.Personal.FullName = "Jane Smith";
        resume.Personal.Email = "jane@example.com";
        resume.Experience.Add(new WorkExperience
        {
            Company = "Example Ltd",
            Title = "Head of Engineering",
            StartDate = new DateOnly(2020, 1, 1),
            IsCurrent = true,
            Achievements = ["Led a TypeScript engineering team through platform change on AWS."],
            Technologies = ["TypeScript", "AWS"]
        });
        resume.Projects.Add(new Project
        {
            Name = "Evidence compiler",
            Description = "Built a provenance-aware career evidence compiler.",
            Technologies = ["C#"],
            Url = "https://github.com/example/evidence-compiler"
        });
        resume.Skills.AddRange([new Skill { Name = "TypeScript" }, new Skill { Name = "AWS" }]);
        var embedder = new DeterministicEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);

        var record = await builder.BuildAsync(resume);
        var serialized = new JobMlParser().Serialize(record);
        var reparsed = new JobMlParser().Parse(serialized);
        var errors = JobMlProcessor.Validate(reparsed)
            .Where(item => item.Severity == JobMlDiagnosticSeverity.Error).ToList();

        Assert.Empty(errors);
        Assert.Equal("career_record", reparsed.Data.Header.Profile);
        Assert.Contains("# Jane Smith", reparsed.Markdown);
        Assert.Contains(reparsed.Data.Claims, claim => claim.Type == "achievement");
        Assert.Contains(reparsed.Data.Sources, source => source.Name == "source.docx");
        var repository = Assert.Single(reparsed.Data.Sources, source => source.Type == "repository");
        Assert.Contains(reparsed.Data.Claims.SelectMany(claim => claim.Evidence),
            evidence => evidence.Type == "repository" && evidence.SourceId == repository.Id);
        Assert.All(reparsed.Data.Concepts.Where(concept => concept.Embedding is not null),
            concept => Assert.Equal(4, concept.Embedding!.Vector.Count));
        Assert.Equal(4, reparsed.Data.RoleCentroids.Count);
        Assert.All(reparsed.Data.RoleCentroids, centroid => Assert.Equal(4, centroid.Vector.Count));
        Assert.Contains("semantic_spaces:", serialized);
        Assert.Contains("role_centroids:", serialized);
        Assert.Contains("source_id:", serialized);
    }

    private sealed class DeterministicEmbedder : IEmbeddingService, IEmbeddingSpaceDescriptor
    {
        public string ModelId => "test/four-dimensional-v1";
        public string Normalization => "l2";
        public string? ModelDigest => "sha256:test";

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var vector = new float[4];
            for (var index = 0; index < text.Length; index++) vector[index % vector.Length] += text[index] % 17;
            var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
            if (magnitude > 0)
                for (var index = 0; index < vector.Length; index++) vector[index] /= magnitude;
            return Task.FromResult(vector);
        }

        public float CosineSimilarity(float[] a, float[] b) => a.Zip(b).Sum(pair => pair.First * pair.Second);
    }
}
