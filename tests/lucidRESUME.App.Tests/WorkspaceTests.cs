using Avalonia.Controls;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.App.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void Job_review_shows_saved_priority_source_and_exclusions_and_hides_stale_results_after_edit()
    {
        using var fixture = new Fixture();
        var vm = fixture.Jobs(new Parser());
        vm.SetContext(null, new JobDescription
        {
            RawText = "Essential C#. Nice to have Docker. Solid experience.",
            RequiredSkills = ["C#"], PreferredSkills = ["Docker"],
            SkillDecisions = [new("C#", "skill", "conservative", SourceText: "Essential C#.", SourceSection: "required"),
                new("Docker", "skill", "conservative", SourceText: "Nice to have Docker.", SourceSection: "preferred"),
                new("SOLID", "noise", "conservative", SourceText: "Solid experience.", SourceSection: "required")]
        });
        Assert.Equal(new[] { "Required", "Preferred" }, vm.RequirementReview.Select(item => item.Priority));
        Assert.Equal("Nice to have Docker.", vm.RequirementReview[1].SourceText);
        Assert.Equal("SOLID", Assert.Single(vm.ExcludedExtraction).Label);
        vm.JobDescriptionText = "A different advert.";
        Assert.Empty(vm.RequirementReview);
        Assert.Empty(vm.ExcludedExtraction);
    }

    [Fact]
    public async Task Role_writing_saves_to_career_preserves_imports_custom_prose_and_source_dates()
    {
        using var fixture = new Fixture();
        var source = ResumeDocument.Create("original.md", "text/markdown", 0);
        source.Personal.FullName = "Author";
        source.Experience.Add(new WorkExperience { Title = "Engineer", Company = "Example", StartDate = new(2020, 2, 17), Achievements = ["Built the API."] });
        await fixture.Store.SaveAsync(new AppState { Resumes = [source] });
        var careerService = new CompleteCareerJobMlService(fixture.Store, fixture.Builder);
        var career = (await careerService.GetAsync())!;
        var file = new JobMlParser().Parse(career.JobMlSource!);
        file = new(file.Markdown + "\n\n## Volunteering\n\nOriginal author account outside structured roles.", file.Data);
        file.Data.Extensions ??= [];
        file.Data.Extensions["another-tool.note"] = "Keep this extension";
        var claim = file.Data.Claims.First(claim => claim.Type == "experience");
        claim.Evidence.Add(new() { Type = "article", Uri = "https://example.com/evidence" });
        await fixture.Store.MutateAsync(state =>
        {
            state.CompleteCareer!.Transcript.JobMlSource = new JobMlParser().Serialize(file);
            state.CompleteCareer.Transcript.CanonicalMarkdown = file.Markdown;
        });
        var vm = fixture.Career();
        await vm.LoadAsync();
        vm.SelectedRole!.Prose = "Designed the API and documented its recovery workflow.\n\nAdded integration tests for failure handling.";
        vm.SelectedRole.SkillsText = "C#, SQL Server";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.HasChanges);
        var saved = await fixture.Store.LoadAsync();
        Assert.Equal("Built the API.", saved.Resumes[0].Experience[0].Achievements[0]);
        var role = Assert.Single(saved.GetCompleteTranscript()!.Experience);
        Assert.Equal(source.Experience[0].Id, role.Id);
        Assert.Equal(new DateOnly(2020, 2, 17), role.StartDate);
        Assert.Equal(2, role.Achievements.Count);
        Assert.Contains("SQL Server", role.Technologies);
        var published = new JobMlParser().Parse(saved.GetCompleteTranscript()!.JobMlSource!);
        Assert.Contains("Original author account outside structured roles.", published.Markdown);
        Assert.Contains("another-tool.note", published.Data.Extensions!.Keys);
        Assert.Contains(published.Data.Claims.SelectMany(claim => claim.Evidence), evidence => evidence.Uri == "https://example.com/evidence");
        Assert.DoesNotContain(JobMlProcessor.Validate(published), item => item.Severity == JobMlDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Complete_prose_can_be_edited_and_invalid_headings_do_not_discard_work()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        vm.FullName = "Updated Author";
        vm.EditorTab = 1;
        vm.CompleteProse = vm.CompleteProse.Replace("Built the API.", "Built the API and added operational documentation.");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.StartsWith("# Updated Author", (await fixture.Store.LoadAsync()).GetCompleteTranscript()!.CanonicalMarkdown);
        Assert.Contains("operational documentation", (await fixture.Store.LoadAsync()).GetCompleteTranscript()!.Experience[0].Achievements[0]);
        vm.CompleteProse = vm.CompleteProse.Replace("### Engineer", "### New title");
        var draft = vm.CompleteProse;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.HasChanges);
        Assert.Equal(draft, vm.CompleteProse);
    }

    [Fact]
    public async Task Concurrent_career_change_is_not_overwritten_and_local_draft_is_kept()
    {
        using var fixture = new Fixture(); await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        vm.SelectedRole!.Prose = "Local account of integration work.";
        await fixture.Store.MutateAsync(state => state.CompleteCareer!.Transcript.Personal.Summary = "Changed elsewhere.");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("changed", vm.ErrorMessage);
        Assert.True(vm.HasChanges);
        Assert.Equal("Local account of integration work.", vm.SelectedRole.Prose);
        Assert.Equal("Changed elsewhere.", (await fixture.Store.LoadAsync()).GetCompleteTranscript()!.Personal.Summary);
    }

    [Fact]
    public async Task Derived_search_refresh_is_not_treated_as_a_concurrent_author_edit()
    {
        using var fixture = new Fixture(); await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        vm.SelectedRole!.Prose = "Maintained the production API and its recovery documentation.";
        await fixture.Store.MutateAsync(state => state.CompleteCareer!.Transcript.ProseIndex.AnalysedAt = DateTimeOffset.UtcNow.AddMinutes(1));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public async Task Career_navigation_retains_role_objects_and_refreshes_job_choices_without_losing_drafts()
    {
        using var fixture = new Fixture(); await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        var selected = vm.SelectedRole;
        await vm.LoadAsync();
        Assert.Same(selected, vm.SelectedRole);
        selected!.Prose = "Unsaved author writing.";
        await fixture.Store.MutateAsync(state => state.Jobs.Add(new JobDescription
            { JobId = Guid.NewGuid(), Title = "New target", Company = "Example" }));
        await vm.LoadAsync();
        Assert.Same(selected, vm.SelectedRole);
        Assert.Equal("Unsaved author writing.", vm.SelectedRole!.Prose);
        Assert.Equal("New target", Assert.Single(vm.TargetJobs).Title);
    }

    [Fact]
    public async Task Reviewed_role_corrections_and_anchor_survive_a_changed_source_import()
    {
        using var fixture = new Fixture(); await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        vm.SelectedRole!.Title = "Development Team Lead";
        vm.SelectedRole.IsCareerAnchor = true;
        vm.SelectedRole.Prose = "Led delivery and wrote the production services.";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        await fixture.Store.MutateAsync(state => state.Resumes[0].Experience[0].Achievements.Add("Newly imported source detail."));
        var saved = await fixture.Store.LoadAsync();
        Assert.Contains(saved.GetCompleteTranscript()!.Experience, role => role.Title == "Development Team Lead" && role.IsCareerAnchor);
        Assert.Contains(saved.GetCompleteTranscript()!.Experience.SelectMany(role => role.Achievements), prose => prose == "Led delivery and wrote the production services.");
        Assert.Equal("Engineer", saved.Resumes[0].Experience[0].Title);
    }

    [Fact]
    public async Task Evidence_prompts_show_the_saved_account_and_a_concrete_question_for_the_selected_role()
    {
        using var fixture = new Fixture(); await fixture.SeedAsync();
        var vm = fixture.Career(); await vm.LoadAsync();
        Assert.NotEmpty(vm.EvidencePrompts);
        Assert.All(vm.EvidencePrompts, prompt =>
        {
            Assert.Contains("Built the API.", prompt.ExistingEvidence);
            Assert.False(string.IsNullOrWhiteSpace(prompt.Question));
        });
    }

    [Fact]
    public async Task Empty_install_can_write_a_career_without_importing_a_resume()
    {
        using var fixture = new Fixture();
        var vm = fixture.Career(); await vm.LoadAsync();
        vm.FullName = "New Author"; vm.AddRoleCommand.Execute(null);
        vm.SelectedRole!.Title = "Developer"; vm.SelectedRole.Company = "Example";
        vm.SelectedRole.Prose = "Built services and supported their operation.";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        var saved = await fixture.Store.LoadAsync();
        Assert.Empty(saved.Resumes);
        Assert.Equal("New Author", saved.GetCompleteTranscript()!.Personal.FullName);
        Assert.Single(saved.GetCompleteTranscript()!.Experience);
    }

    [Fact]
    public async Task Job_intake_extracts_once_accepts_corrections_keeps_url_and_updates_without_duplicates()
    {
        using var fixture = new Fixture(); var parser = new Parser(); var vm = fixture.Jobs(parser);
        vm.JobDescriptionText = "Backend lead vacancy. Build C# APIs.";
        await vm.ExtractJobDetailsCommand.ExecuteAsync(null);
        Assert.True(vm.HasReviewedDetails); Assert.Equal("Parsed role", vm.JobTitle);
        vm.JobTitle = "Development Team Lead"; vm.Company = "Kyckr";
        vm.JobSourceUrl = "https://www.linkedin.com/jobs/view/123";
        await vm.SaveJobCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        var job = Assert.Single((await fixture.Store.LoadAsync()).Jobs);
        Assert.Equal("Development Team Lead", job.Title); Assert.Equal("Kyckr", job.Company);
        Assert.Equal(JobSourceType.LinkedIn, job.Source.Type);
        Assert.Contains("linkedin.com", job.Source.Url);
        Assert.Equal(1, parser.Calls);
        vm.JobTitle = "Corrected title";
        await vm.SaveJobCommand.ExecuteAsync(null);
        var updated = Assert.Single((await fixture.Store.LoadAsync()).Jobs);
        Assert.Equal(job.JobId, updated.JobId); Assert.Equal("Corrected title", updated.Title);
        Assert.Equal(1, parser.Calls);
    }

    [Fact]
    public async Task Missing_metadata_and_bad_url_are_actionable_and_do_not_save_partial_jobs()
    {
        using var fixture = new Fixture(); var vm = fixture.Jobs(new Parser { MissingFields = true });
        vm.JobDescriptionText = "An advert without identity fields";
        await vm.ExtractJobDetailsCommand.ExecuteAsync(null);
        await vm.SaveJobCommand.ExecuteAsync(null);
        Assert.Contains("title and company", vm.ErrorMessage);
        Assert.Empty((await fixture.Store.LoadAsync()).Jobs);
        vm.JobTitle = "Lead"; vm.Company = "Example"; vm.JobSourceUrl = "not a URL";
        await vm.SaveJobCommand.ExecuteAsync(null);
        Assert.Contains("https://", vm.ErrorMessage);
        Assert.Empty((await fixture.Store.LoadAsync()).Jobs);
    }

    [Fact]
    public async Task Switching_jobs_retains_unsaved_paste_and_tracking_does_not_duplicate_applications()
    {
        using var fixture = new Fixture(); var vm = fixture.Jobs(new Parser());
        var existing = JobDescription.Create("Existing advert", new()); existing.Title = "Existing"; existing.Company = "Other";
        vm.JobDescriptionText = "My unsaved new advert";
        vm.SetContext(null, existing);
        vm.NewJobCommand.Execute(null);
        Assert.Equal("My unsaved new advert", vm.JobDescriptionText);
        await vm.ExtractJobDetailsCommand.ExecuteAsync(null);
        await vm.TrackJobCommand.ExecuteAsync(null); await vm.TrackJobCommand.ExecuteAsync(null);
        Assert.Single((await fixture.Store.LoadAsync()).Applications);
    }

    [Fact]
    public void View_locator_reuses_page_instances_so_navigation_does_not_recreate_controls()
    {
        var locator = new lucidRESUME.ViewLocator();
        var vm = new UnmappedViewModel();
        Assert.Same(locator.Build(vm), locator.Build(vm));
        Assert.NotSame(locator.Build(vm), locator.Build(new UnmappedViewModel()));
    }
    private sealed class UnmappedViewModel : lucidRESUME.ViewModels.ViewModelBase { }
    private sealed class Parser : IJobSpecParser
    {
        public int Calls; public bool MissingFields;
        public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new JobDescription { RawText = text, Title = MissingFields ? null : "Parsed role", Company = MissingFields ? null : "Parsed company", RequiredSkills = ["C#"] }); }
        public Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
    private sealed class EmptyCoverage : ICoverageAnalyser
    {
        public Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job, CancellationToken ct = default) => Task.FromResult(new CoverageReport([], CompanyType.Unknown, DateTimeOffset.UtcNow));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid() + ".json");
        public JsonAppStore Store { get; }
        public CareerRecordJobMlBuilder Builder { get; }
        public Fixture()
        {
            Store = new(_path); var embedder = new EmptyEmbedder(); var taxonomy = new SkillTaxonomyService(embedder);
            Builder = new(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
        }
        public CareerWorkspacePageViewModel Career() => new(Store, Builder, new EmptyCoverage());
        public ApplyPageViewModel Jobs(IJobSpecParser parser) => new(null!, new EmptyCoverage(), parser, Store, null!, [], Builder);
        public async Task SeedAsync()
        {
            var source = ResumeDocument.Create("source.md", "text/markdown", 0); source.Personal.FullName = "Author";
            source.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Built the API."] });
            await Store.SaveAsync(new AppState { Resumes = [source] });
            await new CompleteCareerJobMlService(Store, Builder).GetAsync();
        }
        public void Dispose() { Store.Dispose(); if (File.Exists(_path)) File.Delete(_path); }
    }
}
