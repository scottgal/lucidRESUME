using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Core.Tests;

public sealed class CompleteCareerJobMlTests
{
    [Fact]
    public void Invalid_legacy_snapshot_retains_the_archive_and_requires_review_without_blocking_migration()
    {
        var source = ResumeDocument.Create("legacy.md", "text/markdown", 0);
        source.Skills.Add(new Skill { Name = "SQL" });
        source.JobMlSource = "invalid legacy snapshot";
        var state = new AppState { Resumes = [source] };
        var career = state.GetCompleteTranscript()!;
        Assert.Equal(source.JobMlSource, state.Resumes[0].JobMlSource);
        Assert.All(career.EvidenceLedger.Claims, claim => Assert.Equal("required", claim.Review));
        Assert.Single(state.CompleteCareer!.ReviewWarnings);
    }

    [Fact]
    public async Task Full_record_persists_claims_sources_embeddings_and_reuses_vectors_after_a_career_change()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var source = ResumeDocument.Create("career.md", "text/markdown", 0);
            source.Experience.Add(new WorkExperience { Company = "Example", Title = "Engineer", Technologies = ["SQL"],
                Achievements = ["Maintained SQL services."] });
            await store.SaveAsync(new AppState { Resumes = [source] });
            var embeddings = new CountingEmbedder();
            var taxonomy = new SkillTaxonomyService(embeddings);
            var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embeddings, taxonomy), taxonomy, embeddings);
            var service = new CompleteCareerJobMlService(store, builder);
            var first = (await service.GetAsync())!;
            var parsed = new JobMlParser().Parse(first.JobMlSource!);
            Assert.Contains(parsed.Data.Claims, claim => claim.Statement == "Maintained SQL services.");
            Assert.NotEmpty(parsed.Data.Sources);
            Assert.All(parsed.Data.Concepts, concept => Assert.NotNull(concept.Embedding));
            var calls = embeddings.Calls;
            var again = (await service.GetAsync())!;
            Assert.Equal(first.JobMlSource, again.JobMlSource);
            Assert.Equal(calls, embeddings.Calls);
            await store.MutateAsync(state => state.Overrides.PersonalInfoOverrides[nameof(PersonalInfo.Email)] = "reviewed@example.com");
            await service.GetAsync();
            Assert.Equal(calls, embeddings.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Reviewed_role_prose_edits_update_the_authoritative_role_without_rewriting_imports()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new WorkExperience { Company = "Example", Title = "Engineer", Achievements = ["Maintained SQL services."] });
        var state = new AppState { Resumes = [source] };
        var career = state.GetCompleteTranscript()!;
        var embeddings = new CountingEmbedder();
        var taxonomy = new SkillTaxonomyService(embeddings);
        var file = await new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embeddings, taxonomy), taxonomy, embeddings).BuildAsync(career);
        var claim = Assert.Single(file.Data.Claims, claim => claim.Type == "achievement");
        var edited = "Maintained SQL services and reviewed recovery procedures.";
        var old = claim.Statement;
        claim.Statement = edited;
        file = file with { Markdown = file.Markdown.Replace(old, edited) };
        var prose = claim.Evidence.Single(e => e.Type == "prose");
        prose.Selector!.Exact = edited;
        prose.Fingerprint!.Text = MarkdownEvidenceIndex.Fingerprint(edited);
        ReviewedEvidenceSnapshot.ApplyProseEdits(career, file);
        career.JobMlSource = new JobMlParser().Serialize(file);
        ReviewedEvidenceSnapshot.Apply(career);
        Assert.Contains(edited, career.Experience[0].Achievements);
        Assert.Equal(old, source.Experience[0].Achievements[0]);
        Assert.Contains(career.EvidenceLedger.Claims, c => c.Statement == edited && c.Review == "accepted");
        Assert.Contains(career.EvidenceLedger.Evidence, e => e.Text == edited && e.ExtractionMethod == "author-edit");
    }

    private sealed class CountingEmbedder : IEmbeddingService, IEmbeddingSpaceDescriptor
    {
        public int Calls { get; private set; }
        public string ModelId => "test-model";
        public string ModelDigest => "test-digest";
        public string Normalization => "l2";
        public float CosineSimilarity(float[] a, float[] b) => 0;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new[] { 1f, 0f }); }
    }
}
