using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Persistence;
using lucidRESUME.JobML;

namespace lucidRESUME.AI.Tests;

public sealed class ProseRecompositionTests
{
    [Fact]
    public async Task Ingest_and_edit_embed_only_changed_chunks_and_save_the_analysis()
    {
        var path = Path.Combine(Path.GetTempPath(), "prose-ingest-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var embedder = new Counter();
            using var store = new SqliteAppStore(path, proseEnricher: new CareerProseAnalyser(embedder));
            var source = ResumeDocument.Create("source.md", "text/markdown", 0);
            source.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements = ["Built Qdrant retrieval.", "Added billing retry queues."] });
            await store.SaveAsync(new() { Resumes = [source] });
            var initial = (await store.LoadAsync()).CompleteCareer!.Transcript;
            Assert.Equal(2, embedder.Calls);
            Assert.All(initial.ProseIndex.Segments, chunk => Assert.NotEmpty(chunk.Embedding));
            await store.MutateAsync(state => state.Profile.CareerGoals = "Build reliable platforms");
            Assert.Equal(2, embedder.Calls);
            await store.MutateAsync(state => state.CompleteCareer!.Transcript.Experience[0].Achievements.Add("Designed tool authorization allowlists."));
            var edited = (await store.LoadAsync()).CompleteCareer!.Transcript;
            Assert.Equal(3, embedder.Calls);
            Assert.Equal(3, edited.ProseIndex.Segments.Count);
            await new CareerProseAnalyser(embedder).AnalyseAsync(edited);
            Assert.Equal(3, embedder.Calls); // Generation reuses the persisted analysis.
            Assert.Single(await store.SearchAsync(edited.ProseIndex.SearchRevision, "authorization"));
            var extra = ResumeDocument.Create("extra.md", "text/markdown", 0);
            extra.Experience.Add(new() { Company = "Other", Title = "Research Developer", Achievements = ["Built FastAPI prototypes."] });
            await store.MutateAsync(state => state.AddOrReplaceResume(extra));
            Assert.Equal(4, embedder.Calls); // Reconciliation carries unchanged career embeddings forward.
            embedder.ModelId = "replacement";
            await store.MutateAsync(_ => { });
            Assert.Equal(8, embedder.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Rare_terms_rank_concrete_evidence_above_generic_overlap_and_keep_signal_provenance()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        var role = new WorkExperience
        {
            Company = "Example",
            Title = "Engineer",
            Achievements =
            ["Maintained production services.", "Implemented idempotency authorization controls for ticket updates."]
        };
        source.Experience.Add(role);
        source.Experience.AddRange(Enumerable.Range(0, 8).Select(i => new WorkExperience
        { Company = "Other " + i, Title = "Engineer", Achievements = ["Maintained production services."] }));
        var index = CareerProseLexicalIndex.Build(source);
        var detailed = index.Segments.Single(chunk => chunk.Text.Contains("idempotency"));
        var selected = Assert.Single(CareerProseRetriever.Select(index,
            [new("safety", "production services idempotency authorization", [])], $"experience:{role.Id:N}", 1,
            fullText: new Dictionary<string, IReadOnlyList<ProseSearchHit>> { ["safety"] = [new(detailed.Id, 1)] }));
        Assert.Equal(detailed.Id, selected.Segment.Id);
        var signal = Assert.Single(selected.Signals);
        Assert.True(signal.TfIdf > 0);
        Assert.Equal(1, signal.FullTextRank);
        Assert.Equal(0, signal.Semantic);
        Assert.NotEmpty(selected.Segment.ClaimIds);
    }

    [Fact]
    public void Section_plan_preserves_requirement_kind_and_late_job_detail_without_claiming_coverage()
    {
        var job = new JobDescription
        {
            RequiredSkills = ["C#"],
            Responsibilities = ["Design PostgreSQL backups and recovery drills."],
            Competencies = [new("Safety", "Own tool authorization controls.", RequirementPriority.Required)],
            PreferredSkills = ["Kubernetes"],
            RawText = new string('x', 3200) + "\nDesign PostgreSQL recovery drills using approved rollback procedures."
        };
        var requirements = ProseJobRequirements.Build(job);
        Assert.Contains(requirements, item => item.Kind == RequirementKind.Required && item.Text.Contains("authorization"));
        Assert.Contains(requirements, item => item.Kind == RequirementKind.Responsibility && item.Text.Contains("backups"));
        Assert.Contains(requirements, item => item.Kind == RequirementKind.Preferred && item.Text == "Kubernetes");
        var claim = new SelectedClaim(new JobMlClaim { Id = "pg", Subject = "role", Statement = "Designed PostgreSQL backups and recovery drills." }, "Engineer", "", ["source-pg"], 1, []);
        var plan = ProseJobRequirements.Plan(requirements, [claim], new());
        Assert.Contains(plan.Focus, item => item.Text.Contains("approved rollback") && item.UnmatchedTerms.Contains("rollback"));
        Assert.DoesNotContain(plan.Focus, item => item.Text.Contains("Kubernetes") || item.Text.Contains("authorization"));
        Assert.All(plan.Focus, item => Assert.Equal("pg", Assert.Single(item.SourceClaimIds)));
        Assert.Equal("pg", Assert.Single(plan.DetailClaimIds));
        Assert.Empty(plan.ForPass(CompositionPass.Tighten).Focus); // Reusable pass remains job-neutral.
        Assert.NotEmpty(plan.ForPass(CompositionPass.HumanVoice).Focus);
    }

    [Fact]
    public void One_generic_token_does_not_make_a_whole_responsibility_a_section_focus()
    {
        var claim = new SelectedClaim(new JobMlClaim { Id = "source", Statement = "Maintained a production service." }, "Engineer", "", ["e"], 1, []);
        var plan = ProseJobRequirements.Plan([new("slo", "Own production reliability and SLO error budgets.", RequirementKind.Required, "")], [claim], new());
        Assert.Empty(plan.Focus);
    }

    [Fact]
    public void A_large_job_keeps_explicit_must_haves_and_distinctive_late_clauses_in_bounded_retrieval()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new() { Achievements = ["Built Qdrant retrieval and tool authorization controls."] });
        var index = CareerProseLexicalIndex.Build(source);
        var job = new JobDescription
        {
            RequiredSkills = ["C#", "PostgreSQL"],
            Responsibilities = ["Design recovery drills."],
            Competencies = [new("Safety", "Own permission boundaries.", RequirementPriority.Required)],
            RawText = string.Join('\n', Enumerable.Range(0, 400).Select(i => $"Design generic workflow number {i}.")) + "\nDesign Qdrant retrieval and tool authorization controls."
        };
        var full = ProseJobRequirements.Build(job);
        var queries = ProseJobRequirements.SelectForRetrieval(job, full, index);
        Assert.Equal(48, queries.Count);
        Assert.True(full.Count > queries.Count);
        Assert.Contains(queries, item => item.Text == "C#");
        Assert.Contains(queries, item => item.Text == "PostgreSQL");
        Assert.Contains(queries, item => item.Text.Contains("permission boundaries"));
        Assert.Contains(queries, item => item.Text.Contains("Qdrant retrieval"));
    }

    [Fact]
    public void Explicit_non_deployment_keeps_polarity_and_is_not_the_best_positive_production_example()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        var role = new WorkExperience
        {
            Company = "Example",
            Title = "Engineer",
            Achievements =
            ["Deployed a C# agent to production.", "Built a C# agent prototype; it was not deployed to production."]
        };
        source.Experience.Add(role);
        var index = CareerProseLexicalIndex.Build(source);
        var negative = index.Segments.Single(segment => segment.Text.Contains("prototype"));
        Assert.Contains("not", negative.TermFrequency.Keys);
        var production = Assert.Single(CareerProseRetriever.Select(index, [new("live", "C# agent deployed production", [])], $"experience:{role.Id:N}", 1));
        Assert.DoesNotContain("not deployed", production.Segment.Text);
        var prototype = Assert.Single(CareerProseRetriever.Select(index, [new("prototype", "C# agent prototype", [])], $"experience:{role.Id:N}", 1));
        Assert.Equal(negative.Id, prototype.Segment.Id);
    }

    private sealed class Counter : IEmbeddingService, IEmbeddingSpaceDescriptor
    {
        public int Calls { get; private set; }
        public string ModelId { get; set; } = "fixture";
        public string? ModelDigest => "fixture";
        public string Normalization => "unit";
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) { Calls++; return Task.FromResult<float[]>([1, 0]); }
        public float CosineSimilarity(float[] a, float[] b) => 1;
    }
}
