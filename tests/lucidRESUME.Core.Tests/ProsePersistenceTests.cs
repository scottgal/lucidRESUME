using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using System.Text.Json.Nodes;

namespace lucidRESUME.Core.Tests;

public sealed class ProsePersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "career-prose-" + Guid.NewGuid().ToString("N") + ".db");
    public void Dispose()
    {
        File.Delete(_path);
        if (Directory.Exists(_path + ".prose-search")) Directory.Delete(_path + ".prose-search", true);
    }

    [Fact]
    public async Task Save_indexes_only_canonical_accepted_prose_and_removes_rejected_chunks()
    {
        using var store = new SqliteAppStore(_path);
        var source = ResumeDocument.Create("source.md", "text/markdown", 0);
        source.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements = ["Built Qdrant retrieval in C#.", "Built a C++ prototype."] });
        await store.SaveAsync(new() { Resumes = [source] });
        var career = (await store.LoadAsync()).CompleteCareer!.Transcript;
        var index = career.ProseIndex;
        Assert.NotEmpty(index.DocumentFrequency);
        Assert.All(index.Segments, segment => { Assert.NotEmpty(segment.TermFrequency); Assert.NotEmpty(segment.ClaimIds); Assert.NotEmpty(segment.EvidenceIds); });
        var hit = Assert.Single(await store.SearchAsync(index.SearchRevision, "C#"));
        Assert.Contains(index.Segments, segment => segment.Id == hit.SegmentId && segment.Text.Contains("Qdrant"));
        Assert.Single(await store.SearchAsync(index.SearchRevision, "C++"));
        await store.SearchAsync(index.SearchRevision, "C# OR NEAR(\"Qdrant\") NOT ''); DROP TABLE resumes;--"); // Literal terms, not MATCH/SQL syntax.
        await store.MutateAsync(state =>
            state.CompleteCareer!.Transcript.EvidenceLedger.Claims.Single(claim => claim.Statement.Contains("Qdrant") && claim.Kind == "achievement").Review = "rejected");
        var updated = (await store.LoadAsync()).CompleteCareer!.Transcript.ProseIndex;
        Assert.Empty(await store.SearchAsync(index.SearchRevision, "Qdrant")); // Stale snapshot cannot search the current corpus.
        Assert.Empty(await store.SearchAsync(updated.SearchRevision, "Qdrant"));
        Assert.Single(await store.SearchAsync(updated.SearchRevision, "C++"));
        Assert.Single((await store.LoadAsync()).Resumes); // Original import remains archived.
    }

    [Fact]
    public async Task Updating_one_passage_preserves_unchanged_chunk_fingerprints_and_reopen_search()
    {
        string revision;
        using (var store = new SqliteAppStore(_path))
        {
            var source = ResumeDocument.Create("source.md", "text/markdown", 0);
            source.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements = ["Built Qdrant retrieval."] });
            await store.SaveAsync(new() { Resumes = [source] });
            var original = (await store.LoadAsync()).CompleteCareer!.Transcript.ProseIndex;
            var id = Assert.Single(original.Segments).Id;
            var row = ReadRow(id);
            await store.MutateAsync(state => state.CompleteCareer!.Transcript.Experience[0].Achievements.Add("Added PostgreSQL recovery drills."));
            revision = (await store.LoadAsync()).CompleteCareer!.Transcript.ProseIndex.SearchRevision;
            Assert.Equal(row, ReadRow(id));
            Assert.Single(await store.SearchAsync(revision, "PostgreSQL recovery"));
        }
        using var reopened = new SqliteAppStore(_path);
        Assert.Single(await reopened.SearchAsync(revision, "Qdrant"));
        Assert.Single(await reopened.SearchAsync(revision, "PostgreSQL"));
    }

    [Fact]
    public async Task Embedding_does_not_lock_out_reads_and_a_newer_edit_wins_over_an_old_result()
    {
        var enricher = new PausedEnricher();
        using var store = new SqliteAppStore(_path, proseEnricher: enricher);
        var source = ResumeDocument.Create("source.md", "text/markdown", 0);
        source.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements = ["Built Qdrant retrieval."] });
        var firstSave = store.SaveAsync(new() { Resumes = [source] });
        await enricher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var persisted = await store.LoadAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(await store.SearchAsync(persisted.CompleteCareer!.Transcript.ProseIndex.SearchRevision, "Qdrant"));
        var edit = store.MutateAsync(state => state.CompleteCareer!.Transcript.Experience[0].Achievements.Add("Added PostgreSQL recovery drills."));
        var during = await store.LoadAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("PostgreSQL", string.Join(' ', during.CompleteCareer!.Transcript.Experience[0].Achievements));
        enricher.Release.SetResult();
        await Task.WhenAll(firstSave, edit).WaitAsync(TimeSpan.FromSeconds(5));
        var final = (await store.LoadAsync()).CompleteCareer!.Transcript.ProseIndex;
        Assert.Equal("embedded:" + final.SearchRevision, final.EmbeddingRevision);
        Assert.Equal(2, final.Segments.Count);
        Assert.All(final.Segments, segment => Assert.Equal(new float[] { 1, 0 }, segment.Embedding));
    }

    [Fact]
    public async Task Hybrid_search_honours_role_scope_and_model_identity()
    {
        using var store = new SqliteAppStore(_path, proseEnricher: new VectorEnricher());
        var source = ResumeDocument.Create("source.md", "text/markdown", 0);
        source.Experience.Add(new() { Company = "First", Title = "Engineer", Achievements = ["Built C# retry workers."] });
        source.Experience.Add(new() { Company = "Second", Title = "Engineer", Achievements = ["Built C++ retry workers."] });
        await store.SaveAsync(new() { Resumes = [source] });
        var index = (await store.LoadAsync()).CompleteCareer!.Transcript.ProseIndex;
        var first = index.Segments.Single(segment => segment.Text.Contains("C#"));
        var query = new ProseSearchQuery(index.SearchRevision, "retry", first.SubjectId, "achievement",
            [0, 1], "test-vectors", 1);
        Assert.Equal(first.Id, Assert.Single(await store.SearchAsync(query)).SegmentId);
        Assert.Equal(first.Id, Assert.Single(await store.SearchAsync(query with { EmbeddingSpace = "different-model" })).SegmentId);
        Assert.Empty(await store.SearchAsync(query with { SubjectId = "experience:unknown" }));
        Assert.Single(await store.SearchAsync(query with { Embedding = [0, 0] })); // Invalid vectors use lexical search.
    }

    [Fact]
    public async Task Corrupt_derived_manifest_is_rebuilt_from_canonical_evidence()
    {
        using (var store = new SqliteAppStore(_path))
        {
            var source = ResumeDocument.Create("source.md", "text/markdown", 0);
            source.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements = ["Built Qdrant retrieval."] });
            await store.SaveAsync(new() { Resumes = [source] });
        }
        File.WriteAllText(Path.Combine(_path + ".prose-search", "manifest.json"), "broken JSON");
        using var reopened = new SqliteAppStore(_path);
        var index = (await reopened.LoadAsync()).CompleteCareer!.Transcript.ProseIndex;
        await reopened.PrepareAsync(index);
        Assert.Single(await reopened.SearchAsync(index.SearchRevision, "Qdrant"));
    }

    private sealed class VectorEnricher : ICareerProseEnricher
    {
        public Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false)
        {
            foreach (var segment in source.ProseIndex.Segments)
            {
                segment.Embedding = segment.Text.Contains("C#") ? [1, 0] : [0, 1];
                segment.EmbeddingSpace = "test-vectors";
            }
            source.ProseIndex.EmbeddingSpace = "test-vectors";
            source.ProseIndex.EmbeddingRevision = "embedded:" + source.ProseIndex.SearchRevision;
            return Task.FromResult(source.ProseIndex);
        }
    }

    private string ReadRow(string id) => JsonNode.Parse(File.ReadAllText(
        Path.Combine(_path + ".prose-search", "manifest.json")))!["Chunks"]![id]!.GetValue<string>();

    private sealed class PausedEnricher : ICareerProseEnricher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            foreach (var segment in source.ProseIndex.Segments) segment.Embedding = [1, 0];
            source.ProseIndex.EmbeddingRevision = "embedded:" + source.ProseIndex.SearchRevision;
            return source.ProseIndex;
        }
    }
}
