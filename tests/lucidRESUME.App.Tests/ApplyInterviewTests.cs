using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Matching;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Options;

namespace lucidRESUME.App.Tests;

public sealed class ApplyInterviewTests
{
    [Fact]
    public async Task Project_pauses_for_gaps_and_reviewed_answer_is_saved_to_source_role()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-interview-" + Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
            resume.RawMarkdown = "Original source with my volunteering history.";
            resume.Experience.Add(new WorkExperience { Title = "Engineer", Company = "Example" });
            var state = new AppState();
            state.AddOrReplaceResume(resume);
            await store.SaveAsync(state);
            var embedder = new EmptyEmbedder();
            var taxonomy = new SkillTaxonomyService(embedder);
            var careerBuilder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
            var coverage = new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())),
                Options.Create(new CoverageOptions()));
            // The compressor is deliberately absent: generation must stop before invoking it.
            var vm = new ApplyPageViewModel(null!, coverage, new NoReparseParser(), store, null!, [], careerBuilder);
            var job = new JobDescription { Title = "Platform Engineer", RawText = "Role from the jobs page", RequiredSkills = ["AWS"] };
            vm.SetContext(resume, job);
            await vm.SmartCompressCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.True(vm.HasInterview);
            Assert.False(vm.HasResult);
            Assert.Equal("AWS", vm.InterviewRequirement);
            Assert.Null(vm.SelectedInterviewRole);
            vm.SelectedInterviewRole = Assert.Single(vm.InterviewRoles);
            vm.InterviewAction = "Automated AWS recovery";
            vm.InterviewOutcome = "Reduced outages";
            vm.DraftInterviewProseCommand.Execute(null);
            await vm.AcceptInterviewCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            var savedState = await store.LoadAsync();
            var saved = savedState.GetCompleteTranscript()!;
            Assert.Empty(savedState.SelectedResume!.Experience[0].InterviewEntries);
            Assert.Equal(resume.RawMarkdown, savedState.SelectedResume!.RawMarkdown);
            Assert.Single(saved.Experience[0].InterviewEntries);
            Assert.Contains("Reduced outages", saved.CanonicalMarkdown);
            Assert.Contains("Original source with my volunteering history.", saved.CanonicalMarkdown);
            Assert.False(vm.HasInterview);
            Assert.Equal(0, vm.GapCount);
            Assert.NotNull(saved.JobMlSource);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Navigating_to_another_job_during_coverage_cannot_restore_the_old_interview()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-interview-" + Guid.NewGuid() + ".json");
        using var store = new JsonAppStore(path);
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        await store.SaveAsync(new AppState { Resumes = [resume] });
        var coverage = new DelayedCoverage();
        var vm = new ApplyPageViewModel(null!, coverage, new NoReparseParser(), store, null!, [], null!);
        vm.SetContext(resume, new JobDescription { Title = "Old", RawText = "Old job" });
        var pending = vm.SmartCompressCommand.ExecuteAsync(null);
        await coverage.SecondOldCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.SetContext(resume, new JobDescription { Title = "New", RawText = "New job" });
        coverage.Release.SetResult(new CoverageReport([
            new(new("Kubernetes", RequirementPriority.Required), null, null, 0)], CompanyType.Unknown, DateTimeOffset.UtcNow));
        await pending;
        Assert.Equal("New", vm.JobTitle);
        Assert.False(vm.HasInterview);
        Assert.Equal(0, vm.GapCount);
        Assert.Null(vm.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transcript_builder_saves_to_source_owner_and_resumes_without_a_job(bool aggregate)
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-builder-" + Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var first = ResumeDocument.Create("selected.md", "text/markdown", 0);
            first.Experience.Add(new WorkExperience { Title = "Analyst", Company = "Earlier", StartDate = new DateOnly(2010, 1, 1) });
            var source = ResumeDocument.Create("recent.md", "text/markdown", 0);
            source.RawMarkdown = "Original imported evidence.";
            var role = new WorkExperience { Title = "Engineer", Company = "Example", IsCurrent = true, StartDate = new DateOnly(2025, 1, 1) };
            source.Experience.Add(role);
            var state = new AppState { Resumes = [first, source], SelectedResumeId = first.ResumeId };
            await store.SaveAsync(state);
            var embedder = new EmptyEmbedder();
            var taxonomy = new SkillTaxonomyService(embedder);
            var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
            var vm = new ApplyPageViewModel(null!, null!, new NoReparseParser(), store, null!, [], builder);
            vm.SetContext(aggregate ? state.BuildAggregateResume()! : source, null);
            await vm.BuildCompleteTranscriptCommand.ExecuteAsync(null);
            Assert.True(vm.IsTranscriptBuilder);
            Assert.True(vm.HasInterview);
            Assert.Equal(role.Id, vm.SelectedInterviewRole!.Id);
            Assert.Equal(12, vm.InterviewRemaining);
            if (aggregate)
            {
                var choices = vm.InterviewRoles;
                vm.SelectedInterviewRole = choices.Single(choice => choice.Id == first.Experience[0].Id);
                Assert.Same(choices, vm.InterviewRoles);
                Assert.Contains("Earlier", vm.InterviewRequirement);
                vm.SelectedInterviewRole = choices.Single(choice => choice.Id == role.Id);
                Assert.Equal(role.Id, vm.SelectedInterviewRole!.Id);
            }
            vm.InterviewAction = "Designed the API and recovery workflow.";
            vm.InterviewProse = "Designed the API and recovery workflow for customer operations.";
            vm.InterviewSkills = "OpenTelemetry, C#";
            await vm.AcceptInterviewCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            var saved = await store.LoadAsync();
            Assert.Empty(saved.Resumes.Single(r => r.ResumeId == first.ResumeId).Experience[0].InterviewEntries);
            var savedSource = saved.Resumes.Single(r => r.ResumeId == source.ResumeId);
            Assert.Equal(source.RawMarkdown, savedSource.RawMarkdown);
            Assert.Empty(savedSource.Experience[0].InterviewEntries);
            var career = saved.GetCompleteTranscript()!;
            Assert.Single(career.Experience.Single(r => r.Id == role.Id).InterviewEntries);
            Assert.Contains("OpenTelemetry", career.Experience.Single(r => r.Id == role.Id).Technologies);
            Assert.Contains("customer operations", career.CanonicalMarkdown);
            Assert.Equal(11, vm.InterviewRemaining);
            var next = vm.InterviewRequirement;
            await vm.BuildCompleteTranscriptCommand.ExecuteAsync(null);
            Assert.Equal(next, vm.InterviewRequirement);
            Assert.Equal(11, vm.InterviewRemaining);
            if (aggregate)
            {
                vm.SelectedInterviewRole = vm.InterviewRoles.Single(choice => choice.Id == first.Experience[0].Id);
                Assert.Contains("Analyst", vm.InterviewRequirement);
                Assert.Contains("Role scope", vm.InterviewRequirement);
                Assert.Empty(vm.InterviewProse);
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Pasted_advert_populates_title_and_company_without_invalidating_its_own_interview()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-pasted-" + Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
            var state = new AppState();
            state.AddOrReplaceResume(resume);
            await store.SaveAsync(state);
            var coverage = new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())), Options.Create(new CoverageOptions()));
            var vm = new ApplyPageViewModel(null!, coverage, new PastedParser(), store, null!, [], null!);
            vm.SetContext(resume, null);
            vm.JobDescriptionText = "Example advert requiring AWS";
            await vm.SmartCompressCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.Equal("Platform Engineer", vm.JobTitle);
            Assert.Equal("Example", vm.Company);
            Assert.True(vm.HasInterview);
            Assert.Equal("AWS", vm.InterviewRequirement);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Direct_project_uses_all_career_sources_and_personal_overrides()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-complete-" + Guid.NewGuid() + ".json");
        try
        {
            using var store = new JsonAppStore(path);
            var first = ResumeDocument.Create("first.md", "text/markdown", 0);
            first.Experience.Add(new WorkExperience { Title = "Engineer", Company = "First" });
            var second = ResumeDocument.Create("second.md", "text/markdown", 0);
            second.Experience.Add(new WorkExperience { Title = "Lead", Company = "Second" });
            var state = new AppState { Resumes = [first, second], SelectedResumeId = first.ResumeId };
            state.Overrides.PersonalInfoOverrides["Summary"] = "Developed products with agentic LLMs over the past year.";
            await store.SaveAsync(state);
            var coverage = new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())), Options.Create(new CoverageOptions()));
            var vm = new ApplyPageViewModel(null!, coverage, new PastedParser(), store, null!, [], null!);
            vm.JobDescriptionText = "A platform role requiring AWS";
            await vm.SmartCompressCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.True(vm.HasInterview);
            Assert.Equal(2, vm.InterviewRoles.Count);
            Assert.Contains(vm.InterviewRoles, item => item.Id == second.Experience[0].Id);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Successful_client_generation_automatically_archives_resume_job_and_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucid-generated-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonAppStore(Path.Combine(root, "state.json"));
            var source = ResumeDocument.Create("career.md", "text/markdown", 0);
            source.Personal.FullName = "Example Candidate";
            source.Personal.Summary = "Engineering leader building reliable software and developing teams.";
            source.Experience.Add(new WorkExperience
            {
                Title = "Engineer",
                Company = "Original",
                IsCurrent = true,
                Achievements = ["Built reliable software and improved recovery for customer operations."]
            });
            await store.SaveAsync(new AppState { Resumes = [source], SelectedResumeId = source.ResumeId });
            var embedder = new EmptyEmbedder();
            var taxonomy = new SkillTaxonomyService(embedder);
            var compressor = new lucidRESUME.AI.SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
                new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new lucidRESUME.AI.TailoringOptions { ComposeProse = false }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<lucidRESUME.AI.SemanticCompressor>.Instance);
            var exporters = new IResumeExporter[] { new DocxExporter(), new PdfExporter() };
            var archive = new GeneratedResumeArchive(store, exporters, new GeneratedResumeArchivePath(Path.Combine(root, "archive")));
            var coverage = new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())), Options.Create(new CoverageOptions()));
            var vm = new ApplyPageViewModel(compressor, coverage, new NoReparseParser(), store, new ResumeArtifactBuilder(), exporters, null!, archive: archive);
            vm.SetContext(source, new JobDescription { Title = "Engineer", Company = "Target", RawText = "Build reliable software" });
            await vm.SmartCompressCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.True(vm.HasResult);
            var entry = Assert.Single((await store.LoadAsync()).GeneratedResumes);
            Assert.Equal("Target", entry.Job.Company);
            Assert.Contains("recovery", entry.Resume.Experience[0].Achievements[0]);
            Assert.NotEmpty(archive.PdfPages(entry.Id));
            Assert.Contains("Saved to Generated archive", vm.StatusMessage);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class PastedParser : IJobSpecParser
    {
        public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default) => Task.FromResult(new JobDescription
        { Title = "Platform Engineer", Company = "Example", RawText = text, RequiredSkills = ["AWS"] });
        public Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DelayedCoverage : ICoverageAnalyser
    {
        private int _oldCalls;
        public TaskCompletionSource SecondOldCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CoverageReport> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job, CancellationToken ct = default)
        {
            if (job.Title != "Old") return Task.FromResult(new CoverageReport([], CompanyType.Unknown, DateTimeOffset.UtcNow));
            if (Interlocked.Increment(ref _oldCalls) == 2) SecondOldCall.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class NoReparseParser : IJobSpecParser
    {
        public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default) =>
            throw new InvalidOperationException("Pre-parsed job context must be retained.");
        public Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
