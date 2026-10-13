using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Matching;
using lucidRESUME.Matching.Graph;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.App.Tests;

public sealed class SavedJobsTests
{
    [Fact]
    public async Task Browsing_saved_jobs_does_not_rebuild_matches_or_embed_imported_resumes()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-saved-jobs-" + Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var source = ResumeDocument.Create("original.md", "text/markdown", 0);
            source.Personal.FullName = "Example";
            source.Skills.Add(new() { Name = "C#" });
            source.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Built C# APIs."] });
            var state = new AppState();
            state.AddOrReplaceResume(source);
            state.Jobs.Add(new JobDescription { JobId = Guid.NewGuid(), Title = "Applied AI Engineer", RequiredSkills = ["LLMs"] });
            await store.SaveAsync(state);
            var embedder = new UnexpectedEmbedder();
            var vm = new JobsPageViewModel(null!, null!, null!, null!, null!, store, null!, null!,
                new SkillLedgerBuilder(embedder), null!, null!, null!, new SearchQueryGenerator());
            await vm.LoadSavedCommand.ExecuteAsync(null);
            Assert.Equal("Applied AI Engineer", Assert.Single(vm.Jobs).Title);
            Assert.Equal("Match not assessed", vm.Jobs[0].SkillScoreLabel);
            Assert.Null((await store.LoadAsync()).Jobs[0].MatchScore);
            Assert.Equal(0, embedder.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class UnexpectedEmbedder : IEmbeddingService
    {
        public int Calls { get; private set; }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Opening saved jobs must not trigger embedding inference."); }
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
