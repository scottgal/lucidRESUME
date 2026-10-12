using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Core.Tests;

public sealed class CompleteCareerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_career_survives_restart_and_generation_does_not_read_imports(bool sqlite)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + (sqlite ? ".db" : ".json"));
        try
        {
            IAppStore Open() => sqlite ? new SqliteAppStore(path) : new JsonAppStore(path);
            var store = Open();
            var source = Source("One", "C#");
            var state = new AppState { Resumes = [source] };
            await store.SaveAsync(state);
            var id = state.GetCompleteTranscript()!.ResumeId;
            Assert.NotEqual(source.ResumeId, id);
            ((IDisposable)store).Dispose();
            store = Open();
            var loaded = await store.LoadAsync();
            Assert.Equal(id, loaded.GetCompleteTranscript()!.ResumeId);
            // Generation reads the career, even with deliberately unusable source input.
            loaded.Resumes[0].Experience.Clear();
            loaded.Resumes[0].Skills.Clear();
            loaded.Resumes[0].JobMlSource = "invalid published snapshot";
            var career = loaded.GetCompleteTranscript()!;
            Assert.Single(career.Experience);
            Assert.Empty(career.AggregateEvidenceSources);
            var ledger = await new SkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(career);
            Assert.NotNull(ledger.Find("C#"));
            ((IDisposable)store).Dispose();
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix); }
    }

    [Fact]
    public void New_evidence_keeps_canonical_identity_interviews_and_review_decisions()
    {
        var first = Source("First", "C#");
        first.Personal.Email = "canonical@example.com";
        var other = Source("Other", "SQL");
        var state = new AppState { Resumes = [first, other], SelectedResumeId = first.ResumeId };
        var career = state.GetCompleteTranscript()!;
        var id = career.ResumeId;
        RoleInterviewRecorder.Accept(career, first.Experience[0].Id, new RoleInterviewEntry
        { Action = "Built tracing", ReviewedProse = "Built production tracing.", Skills = ["OpenTelemetry"] });
        career.EvidenceLedger.Claims.Single(c => c.Kind == "skill" && c.Statement == "SQL").Review = "rejected";
        first.Experience[0].Achievements.Add("Added a reviewed delivery account.");
        first.Personal.Email = "stale@example.com";
        state.ReconcileCompleteTranscript();
        var updated = state.GetCompleteTranscript()!;
        Assert.Equal(id, updated.ResumeId);
        Assert.Equal("canonical@example.com", updated.Personal.Email);
        Assert.Equal(2, updated.Experience.Count);
        Assert.Single(updated.Experience.Single(r => r.Id == first.Experience[0].Id).InterviewEntries);
        Assert.Contains(updated.EvidenceLedger.Claims, c => c.Statement == "SQL" && c.Review == "rejected");
        Assert.Contains(updated.Experience.SelectMany(r => r.Achievements), a => a.Contains("delivery account"));
    }

    [Fact]
    public async Task Published_rejection_is_honoured_during_migration_and_later_reconciliation()
    {
        var source = Source("One", "Terraform");
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var skills = new SkillLedgerBuilder(embedder, taxonomy);
        var builder = new CareerRecordJobMlBuilder(skills, taxonomy, embedder);
        var file = await builder.BuildAsync(source);
        source.JobMlSource = new JobMlParser().Serialize(file);
        var state = new AppState { Resumes = [source] };
        Assert.NotNull((await skills.BuildAsync(state.GetCompleteTranscript()!)).Find("Terraform"));
        foreach (var claim in file.Data.Claims.Where(c => c.Statement == "Terraform")) claim.Review = "rejected";
        source.JobMlSource = new JobMlParser().Serialize(file);
        state.ReconcileCompleteTranscript();
        Assert.Null((await skills.BuildAsync(state.GetCompleteTranscript()!)).Find("Terraform"));
        // Same migration directly from a legacy source with JobML-only rejection.
        var legacy = new AppState { Resumes = [source] };
        Assert.Null((await skills.BuildAsync(legacy.GetCompleteTranscript()!)).Find("Terraform"));
    }

    [Fact]
    public void Duplicate_accepted_passage_cannot_cite_its_rejected_copy()
    {
        var first = Source("One", "SQL");
        var second = Source("Two", "SQL");
        var rejected = EvidenceLedgerBuilder.EnsureCurrent(second);
        foreach (var claim in rejected.Claims.Where(c => c.Statement == "SQL")) claim.Review = "rejected";
        var rejectedIds = rejected.Claims.Where(c => c.Statement == "SQL").SelectMany(c => c.EvidenceIds).ToHashSet();
        var career = new AppState { Resumes = [first, second] }.GetCompleteTranscript()!;
        var merged = career.EvidenceLedger.Claims.Single(c => c.Kind == "skill" && c.Statement == "SQL");
        Assert.Equal("accepted", merged.Review);
        Assert.DoesNotContain(merged.EvidenceIds, rejectedIds.Contains);
    }

    [Fact]
    public void Identical_prose_in_different_roles_keeps_its_own_source_evidence()
    {
        var first = Source("First", "SQL");
        var second = Source("Second", "SQL");
        first.Experience[0].Achievements.Add("Maintained SQL services.");
        second.Experience[0].Achievements.Add("Maintained SQL services.");
        var career = new AppState { Resumes = [first, second] }.GetCompleteTranscript()!;
        foreach (var role in career.Experience)
        {
            var claim = career.EvidenceLedger.Claims.Single(c => c.Kind == "achievement" && c.SubjectId == $"experience:{role.Id:N}");
            Assert.All(claim.EvidenceIds, id => Assert.Contains($"experience:{role.Id:N}:", id));
        }
    }

    private static ResumeDocument Source(string company, string skill)
    {
        var source = ResumeDocument.Create(company + ".md", "text/markdown", 0);
        source.Experience.Add(new WorkExperience { Title = "Engineer", Company = company,
            StartDate = new DateOnly(2023, 1, 1), IsCurrent = true, Technologies = [skill] });
        source.Skills.Add(new Skill { Name = skill });
        return source;
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public float CosineSimilarity(float[] a, float[] b) => 0;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => Array.Empty<float>()).ToArray());
    }
}
