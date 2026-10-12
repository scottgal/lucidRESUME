using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class ResumeProjectionEditorTests
{
    [Fact]
    public void Job_brief_retains_detailed_competencies_and_distinguishes_same_title_jobs()
    {
        var database = new JobDescription
        {
            Title = "Platform Engineer",
            RequiredSkills = ["C#"],
            Competencies = [new("Reliability", "Own PostgreSQL backups and recovery drills.", Core.Models.Coverage.RequirementPriority.Required)]
        };
        var accessibility = new JobDescription
        {
            Title = "Platform Engineer",
            RequiredSkills = ["C#"],
            Competencies = [new("Reliability", "Own accessible interfaces and keyboard navigation.", Core.Models.Coverage.RequirementPriority.Required)]
        };
        var databaseBrief = ResumeProjectionEditor.BuildJobBrief(database);
        var accessibilityBrief = ResumeProjectionEditor.BuildJobBrief(accessibility);
        Assert.Contains("PostgreSQL backups and recovery drills", databaseBrief);
        Assert.Contains("accessible interfaces and keyboard navigation", accessibilityBrief);
        Assert.NotEqual(databaseBrief, accessibilityBrief);
        Assert.NotEqual(databaseBrief.Split("Job fingerprint: ")[1], accessibilityBrief.Split("Job fingerprint: ")[1]);
    }

    [Theory]
    [InlineData("Supported the service migration and improved recovery.", true)]
    [InlineData("Led the service migration and improved recovery.", false)]
    public async Task Edits_preserve_role_bindings_and_reject_strengthened_ownership(string edit, bool accepted)
    {
        const string original = "Supported a service migration which improved recovery.";
        var source = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var role = new WorkExperience { Title = "Engineer", Company = "Example", Achievements = [original] };
        source.Experience.Add(role);
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var claim = ledger.Claims.Single(claim => claim.Kind == "achievement");
        var projection = ResumeDocument.Create("projected.md", "text/markdown", 0);
        projection.Personal = new PersonalInfo();
        projection.Experience.Add(new WorkExperience { Id = role.Id, Achievements = [original] });
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = source.ResumeId,
            SourceRevision = ledger.SourceRevision,
            Blocks = [new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = "#role:p1" }]
        };
        projection.SetDoclingOutput("## Engineer {#role}\n\n- " + original + "\n", null, null);
        var editor = new ResumeProjectionEditor(new ResumeCompositionOrchestrator([new Editor(edit)], new CompositionValidator()),
            Options.Create(new TailoringOptions { Provider = "test" }));
        await editor.EditAsync(source, projection, new JobDescription { Title = "Engineer", RawText = "Service reliability" });
        Assert.Contains(accepted ? edit : original, projection.RawMarkdown);
        Assert.Equal(accepted ? edit : original, projection.Experience[0].Achievements[0]);
        Assert.Equal(original, source.Experience[0].Achievements[0]);
        Assert.Equal("#role:p1", Assert.Single(projection.Projection.Blocks).ProseRef);
        if (!accepted) Assert.Contains(projection.GenerationWarnings, warning => warning.Contains("ownership"));
    }

    [Theory]
    [InlineData("Email: alex@example.com | Phone: 01234567890 Blog: example.net | GitHub: github.com/alex Technology leader using ASP.NET Core and Azure.", "Technology leader using ASP.NET Core and Azure.")]
    [InlineData("Software engineer building ASP.NET Core applications.", "Software engineer building ASP.NET Core applications.")]
    public void Contact_cleanup_preserves_technical_prose(string source, string expected) =>
        Assert.Equal(expected, SemanticCompressor.CleanSummary(source));

    [Fact]
    public async Task Identical_prose_in_other_roles_is_not_rewritten_by_an_edit_bound_to_one_role()
    {
        const string original = "Built API recovery workflows.";
        const string edit = "Built recovery workflows for the API.";
        var source = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var first = new WorkExperience { Company = "First", Title = "Engineer", Achievements = [original] };
        var second = new WorkExperience { Company = "Second", Title = "Engineer", Achievements = [original] };
        source.Experience.AddRange([first, second]);
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var claim = ledger.Claims.Single(claim => claim.SubjectId == $"experience:{first.Id:N}" && claim.Kind == "achievement");
        var projection = ResumeDocument.Create("out.md", "text/markdown", 0);
        projection.Experience.AddRange([new WorkExperience { Id = first.Id, Achievements = [original] },
            new WorkExperience { Id = second.Id, Achievements = [original] }]);
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = source.ResumeId,
            SourceRevision = ledger.SourceRevision,
            Blocks = [new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = "#first:p1" }]
        };
        projection.SetDoclingOutput("## First {#first}\n\n- " + original + "\n\n## Second {#second}\n\n- " + original, null, null);
        var editor = new ResumeProjectionEditor(new ResumeCompositionOrchestrator([new Editor(edit)], new CompositionValidator()),
            Options.Create(new TailoringOptions { Provider = "test" }));
        await editor.EditAsync(source, projection, new JobDescription());
        Assert.Equal(edit, projection.Experience[0].Achievements[0]);
        Assert.Equal(original, projection.Experience[1].Achievements[0]);
        Assert.Contains("## Second {#second}\n\n- " + original, projection.RawMarkdown);
    }

    [Theory]
    [InlineData("https://linkedin.com/in/", null)]
    [InlineData("https://www.linkedin.com/in/alex/", "https://www.linkedin.com/in/alex/")]
    public void Placeholder_profile_roots_are_omitted(string input, string? expected) =>
        Assert.Equal(expected, SemanticCompressor.CleanProfileUrl(input));

    [Fact]
    public async Task Summary_claims_are_edited_and_synchronized_with_export_fields()
    {
        var source = ResumeDocument.Create("cv.md", "text/markdown", 0);
        source.Personal.Summary = "Engineer with experience building reliable services.";
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var claim = ledger.Claims.Single(claim => claim.Id.EndsWith(":personal:summary", StringComparison.Ordinal));
        var projection = ResumeDocument.Create("out.md", "text/markdown", 0);
        projection.Personal = new PersonalInfo { Summary = source.Personal.Summary };
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = source.ResumeId,
            SourceRevision = ledger.SourceRevision,
            Blocks = [new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = "#summary:p1" }]
        };
        projection.SetDoclingOutput("## Summary {#summary}\n\n" + source.Personal.Summary, null, null);
        const string edit = "Engineer building reliable services.";
        var editor = new ResumeProjectionEditor(new ResumeCompositionOrchestrator([new Editor(edit)], new CompositionValidator()),
            Options.Create(new TailoringOptions { Provider = "test" }));
        await editor.EditAsync(source, projection, new JobDescription());
        Assert.Equal(edit, projection.Personal.Summary);
        Assert.Contains(edit, projection.RawMarkdown);
        Assert.Equal("Engineer with experience building reliable services.", source.Personal.Summary);
    }

    [Theory]
    [InlineData("Remote Oct 2024 – Present", "Remote")]
    [InlineData("Glasgow, UK Jan 2011 – Jul 2011", "Glasgow, UK")]
    [InlineData("London, UK", "London, UK")]
    public void Role_location_does_not_repeat_imported_date_range(string source, string expected) =>
        Assert.Equal(expected, SemanticCompressor.CleanRoleLocation(source));

    private sealed class Editor(string edit) : IResumeCompositionProvider
    {
        public string ProviderId => "test";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CompositionDraft(request.CurrentBlocks.Select(block => block with { Text = edit }).ToList(), []));
    }
}
