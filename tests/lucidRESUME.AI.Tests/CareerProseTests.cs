using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class CareerProseTests
{
    [Fact]
    public async Task Explicit_refresh_can_finish_a_large_index_without_reembedding_unchanged_passages()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new() { Achievements = Enumerable.Range(0, 501).Select(i => $"Implemented workflow {i}.").ToList() });
        var embedder = new CountingEmbedder();
        var analyser = new CareerProseAnalyser(embedder);
        var initial = await analyser.AnalyseAsync(source);
        Assert.Equal(501, initial.Segments.Count);
        Assert.Equal(500, initial.Segments.Count(segment => segment.Embedding.Length > 0));
        var completed = await analyser.AnalyseAsync(source, force: true);
        Assert.Equal(501, completed.Segments.Count(segment => segment.Embedding.Length > 0));
        Assert.Equal(501, embedder.Calls);
    }

    [Fact]
    public async Task Reviewed_structured_account_and_its_declared_skills_are_linked_to_the_original_role()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        var role = new WorkExperience { Title = "Engineer", Company = "Example" };
        source.Experience.Add(role);
        var entry = new RoleInterviewEntry { Action = "Implemented retries", Context = "Billing failures",
            Outcome = "Unmeasured", ReviewedProse = "Implemented billing retries in C#; recovery time was unmeasured.", Skills = ["C#"],
            Account = new() { Ownership = "Individual contributor", Decision = "Durable storage", DeliveryStatus = "Prototype" } };
        Assert.True(RoleInterviewRecorder.Accept(source, role.Id, entry));
        var segment = Assert.Single((await new CareerProseAnalyser(new CountingEmbedder()).AnalyseAsync(source)).Segments);
        Assert.Contains("C#", segment.Skills);
        var account = Assert.Single(segment.Accounts);
        Assert.Equal(role.Id, account.RoleId);
        Assert.Equal(entry.Id, account.InterviewId);
        Assert.Equal("Durable storage", account.Decision);
        Assert.Equal("Prototype", account.DeliveryStatus);
        Assert.Equal(entry.ReviewedProse, account.ReviewedProse);
    }

    [Fact]
    public async Task Unchanged_analysis_is_reused_and_only_changed_passages_are_embedded()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        var role = new WorkExperience { Title = "Engineer", Company = "Example", Achievements = ["Built C# billing queues.", "Added PostgreSQL recovery drills."] };
        source.Experience.Add(role);
        var embedder = new CountingEmbedder();
        var analyser = new CareerProseAnalyser(embedder);
        var first = await analyser.AnalyseAsync(source);
        Assert.Equal(2, embedder.Calls);
        Assert.Same(first, await analyser.AnalyseAsync(source));
        Assert.Equal(2, embedder.Calls);
        role.Achievements[1] = "Added PostgreSQL recovery drills and measured restoration time.";
        var refreshed = await analyser.AnalyseAsync(source);
        Assert.Equal(3, embedder.Calls);
        Assert.NotEqual(first.SourceRevision, refreshed.SourceRevision);
        Assert.Equal(first.Segments[0].Embedding, refreshed.Segments[0].Embedding);
        Assert.Equal("Built C# billing queues.", role.Achievements[0]);
    }

    [Fact]
    public async Task Duplicate_accounts_merge_supports_within_the_role_but_never_across_roles()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience = [new() { Company = "First", Achievements = ["Built billing queues.", "Built billing queues."] },
            new() { Company = "Second", Achievements = ["Built billing queues."] }];
        var index = await new CareerProseAnalyser(new CountingEmbedder()).AnalyseAsync(source);
        Assert.Equal(2, index.Segments.Count);
        Assert.Equal(2, index.Segments[0].ClaimIds.Count);
        Assert.Equal(2, index.Segments[0].EvidenceIds.Count);
        Assert.NotEqual(index.Segments[0].Id, index.Segments[1].Id);
    }

    [Fact]
    public async Task Rejected_claims_are_removed_without_reembedding_other_passages()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new() { Achievements = ["Built billing queues.", "Operated payment recovery."] });
        var embedder = new CountingEmbedder();
        var analyser = new CareerProseAnalyser(embedder);
        await analyser.AnalyseAsync(source);
        source.EvidenceLedger.Claims.First(claim => claim.Statement == "Built billing queues.").Review = "rejected";
        // Review decisions must affect the derived index even when source prose did not change.
        var index = await analyser.AnalyseAsync(source);
        Assert.Single(index.Segments);
        Assert.Equal("Operated payment recovery.", index.Segments[0].Text);
        Assert.Equal(2, embedder.Calls);
    }

    [Fact]
    public async Task Embedding_model_change_invalidates_vectors_and_offline_analysis_retains_prose()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new() { Achievements = ["Built billing queues.", "Operated payment recovery."] });
        var embedder = new CountingEmbedder();
        var analyser = new CareerProseAnalyser(embedder);
        await analyser.AnalyseAsync(source);
        embedder.ModelId = "replacement";
        await analyser.AnalyseAsync(source);
        Assert.Equal(4, embedder.Calls);
        embedder.ModelId = "offline";
        embedder.Offline = true;
        var offline = await analyser.AnalyseAsync(source);
        Assert.Equal(5, embedder.Calls); // Stop retrying the unavailable provider for every passage.
        Assert.Equal(2, offline.Segments.Count);
        Assert.All(offline.Segments, segment => Assert.Empty(segment.Embedding));
    }

    [Fact]
    public void Retrieval_covers_distinct_requirements_and_preserves_role_scope()
    {
        var index = new CareerProseIndex { Segments = [
            new() { Id = "a", SubjectId = "role", Kind = "achievement", Text = "Built billing retry queues." },
            new() { Id = "b", SubjectId = "role", Kind = "achievement", Text = "Built billing retry queues with durable storage." },
            new() { Id = "c", SubjectId = "role", Kind = "achievement", Text = "Implemented audit logging and access authorization." },
            new() { Id = "d", SubjectId = "different-role", Kind = "achievement", Text = "Billing retry queues and audit logging access authorization." }] };
        ProseRequirement[] requirements = [new("recovery", "billing retry queues", []), new("safety", "audit logging access authorization", [])];
        var selected = CareerProseRetriever.Select(index, requirements, "role", 2);
        Assert.Contains(selected, item => item.Segment.Id == "c");
        Assert.All(selected, item => Assert.Equal("role", item.Segment.SubjectId));
        Assert.Contains("recovery", selected.SelectMany(item => item.RequirementIds));
        Assert.Contains("safety", selected.SelectMany(item => item.RequirementIds));
    }

    [Fact]
    public void Complementary_facts_can_be_synthesized_but_ownership_and_status_stay_separate()
    {
        var facts = ProseSynthesisAssembler.Assemble([
            "Built billing retry queues in C#.", "Added billing retry audit trails.",
            "Helped build billing retry monitoring.", "Built a billing retry prototype."],
            new HashSet<string>(["billing", "retry", "queues", "audit", "monitoring"]));
        Assert.Equal(3, facts.Count);
        Assert.Equal(2, facts[0].Count);
        Assert.Single(facts[1]);
        Assert.Single(facts[2]);
    }

    [Fact]
    public void Conflicting_metrics_or_measurement_qualifiers_cannot_be_grouped_as_one_account()
    {
        var facts = ProseSynthesisAssembler.Assemble([
            "Reduced database query latency from 240 ms to 180 ms.",
            "Increased database query latency from 180 ms to 240 ms.",
            "Improved database query latency; the result was unmeasured."],
            new HashSet<string>(["database", "query", "latency"]), 3);
        Assert.Equal(3, facts.Count);
        Assert.All(facts, packet => Assert.Single(packet));
    }

    private sealed class CountingEmbedder : IEmbeddingService, IEmbeddingSpaceDescriptor
    {
        public int Calls { get; private set; }
        public bool Offline { get; set; }
        public string ModelId { get; set; } = "fixture";
        public string Normalization => "unit";
        public string? ModelDigest => "fixture-digest";
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        { Calls++; return Offline ? throw new HttpRequestException("offline") : Task.FromResult<float[]>([1, 0]); }
        public float CosineSimilarity(float[] a, float[] b) => a[0] * b[0];
    }
}
