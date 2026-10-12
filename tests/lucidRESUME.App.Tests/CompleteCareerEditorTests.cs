using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;
using lucidRESUME.Services;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.App.Tests;

public sealed class CompleteCareerEditorTests
{
    [Fact]
    public async Task Complete_editor_preserves_machine_data_and_publishes_reviewed_prose_to_the_career()
    {
        var directory = Path.Combine(Path.GetTempPath(), "career-editor-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new JsonAppStore(Path.Combine(directory, "state.json"));
            var source = ResumeDocument.Create("source.md", "text/markdown", 0);
            source.Experience.Add(new WorkExperience { Title = "Engineer", Company = "Example", Achievements = ["Maintained SQL services."] });
            await store.SaveAsync(new AppState { Resumes = [source] });
            var embedder = new EmptyEmbedder();
            var taxonomy = new SkillTaxonomyService(embedder);
            var service = new CompleteCareerJobMlService(store,
                new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder));
            var career = (await service.GetAsync())!;
            var editor = new JobMlEditorPageViewModel(new JobMlWorkspacePath(Path.Combine(directory, "draft.md")),
                store, [new DocxExporter()], null!, service);
            Assert.True(editor.TryLoadMarkdown(career.JobMlSource!, career.ResumeId));
            var original = new JobMlParser().Parse(editor.DocumentText);
            Assert.Equal("career_record", original.Data.Header.Profile);
            Assert.Equal(new JobMlParser().Parse(career.JobMlSource!).Data.Claims.Count, original.Data.Claims.Count);
            Assert.False(editor.IsDirty);
            var edited = "Maintained SQL services and documented recovery procedures.";
            editor.HumanMarkdown = editor.HumanMarkdown.Replace("Maintained SQL services.", edited);
            Assert.True(editor.IsDirty);
            editor.AcceptEvidenceChangesCommand.Execute(null);
            await editor.PublishReviewedSnapshotCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            var saved = await store.LoadAsync();
            Assert.Contains(edited, saved.GetCompleteTranscript()!.Experience[0].Achievements);
            Assert.Equal("Maintained SQL services.", saved.Resumes[0].Experience[0].Achievements[0]);
            Assert.Contains("Published", editor.StatusMessage);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public float CosineSimilarity(float[] a, float[] b) => 0;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
    }
}
