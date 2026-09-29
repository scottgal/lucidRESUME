using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;

namespace lucidRESUME.Core.Tests;

public sealed class ResumeImportValidationTests
{
    [Fact]
    public void MarkdownParser_ExplicitSummaryDoesNotIncludeContactHeader()
    {
        const string markdown = """
            # Avery Example
            Email: avery@example.test | Phone: 01234 567890
            GitHub: github.com/avery

            ## Executive Summary
            Platform engineer who keeps production systems reliable.

            ## Experience
            ### Example Corp | Engineer | Remote
            2020 - Present
            """;

        var resume = ResumeDocument.Create("avery.txt", "text/plain", markdown.Length);
        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("Platform engineer who keeps production systems reliable.", resume.Personal.Summary);
        Assert.DoesNotContain("avery@example", resume.Personal.Summary);
    }

    [Fact]
    public void MarkdownParser_ExtractsMultipleRolesDatesAndEvidenceBullets()
    {
        const string markdown = """
            # Avery Example

            ## Professional Experience

            ### Northstar Systems | Principal Engineer | London
            January 2022 - Present
            - Led a production platform migration without downtime.
            - Operated Kubernetes workloads serving regulated customers.

            ### Example Labs | Senior Software Engineer | Remote
            March 2018 - December 2021
            - Built ASP.NET Core services backed by PostgreSQL.

            ## Skills
            C#, ASP.NET Core, PostgreSQL, Kubernetes
            """;

        var resume = ResumeDocument.Create("avery.txt", "text/plain", markdown.Length);
        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("Avery Example", resume.Personal.FullName);
        Assert.Equal(2, resume.Experience.Count);
        var current = Assert.Single(resume.Experience, item => item.Company == "Northstar Systems");
        Assert.Equal("Principal Engineer", current.Title);
        Assert.True(current.IsCurrent);
        Assert.Equal(new DateOnly(2022, 1, 1), current.StartDate);
        Assert.Contains(current.Achievements, item => item.Contains("Kubernetes", StringComparison.Ordinal));

        var previous = Assert.Single(resume.Experience, item => item.Company == "Example Labs");
        Assert.Equal(new DateOnly(2018, 3, 1), previous.StartDate);
        Assert.Equal(new DateOnly(2021, 12, 1), previous.EndDate);
        Assert.Contains(resume.Skills, skill => skill.Name == "ASP.NET Core");
    }

    [Fact]
    public async Task DirectDocxParser_ParsesGeneratedResumeAndFindsExperience()
    {
        var source = ResumeDocument.Create("avery.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 0);
        source.Personal.FullName = "Avery Example";
        source.Personal.Email = "avery@example.test";
        source.Personal.Summary = "Platform engineer specialising in reliable production systems, evidence-led delivery, and clear technical leadership across complex migration programmes.";
        source.Skills.AddRange([
            new Skill { Name = "C#", Category = "Languages" },
            new Skill { Name = "ASP.NET Core", Category = "Frameworks" },
            new Skill { Name = "Kubernetes", Category = "Infrastructure" }
        ]);
        source.Experience.Add(new WorkExperience
        {
            Company = "Northstar Systems",
            Title = "Principal Platform Engineer",
            StartDate = new DateOnly(2022, 1, 1),
            IsCurrent = true,
            Technologies = ["C#", "ASP.NET Core", "Kubernetes"],
            Achievements =
            [
                "Led a production platform migration while maintaining customer availability.",
                "Designed observable services and deployment controls for regulated workloads.",
                "Mentored engineers in incident response, performance analysis, and evidence-led delivery."
            ]
        });

        var path = Path.Combine(Path.GetTempPath(), $"lucidresume-parser-{Guid.NewGuid():N}.docx");
        try
        {
            await File.WriteAllBytesAsync(path, await new DocxExporter().ExportAsync(source));
            var parsed = await new DocxDirectParser().ParseAsync(path);

            Assert.NotNull(parsed);
            Assert.True(parsed.Confidence >= 0.5);
            Assert.True(parsed.PlainText.Length > 300);

            var resume = ResumeDocument.Create(Path.GetFileName(path),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                new FileInfo(path).Length);
            MarkdownSectionParser.PopulateSections(resume, parsed.Markdown, parsed.Sections);

            var experience = Assert.Single(resume.Experience);
            Assert.Equal("Northstar Systems", experience.Company);
            Assert.Equal("Principal Platform Engineer", experience.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppState_PreservesSeparateImportsAndBuildsDeduplicatedAggregate()
    {
        var first = ResumeDocument.Create("platform.docx", "application/docx", 100);
        first.Experience.Add(new WorkExperience
        {
            Company = "Northstar Systems",
            Title = "Principal Engineer",
            StartDate = new DateOnly(2022, 1, 1),
            IsCurrent = true,
            Achievements = ["Led the platform migration."],
            ImportSources = ["platform.docx"]
        });
        first.Skills.Add(new Skill
        { Name = "Kubernetes", EndorsementCount = 3, ImportSources = ["platform.docx"] });
        first.Projects.Add(new Project
        {
            Name = "Atlas",
            Description = "Built the platform.",
            Technologies = ["C#"],
            ImportSources = ["platform.docx"]
        });

        var second = ResumeDocument.Create("leadership.docx", "application/docx", 100);
        second.Experience.Add(new WorkExperience
        {
            Company = "Northstar Systems Ltd",
            Title = "Principal Platform Engineer",
            StartDate = new DateOnly(2022, 2, 1),
            IsCurrent = true,
            Achievements = ["Led the platform migration.", "Mentored eight engineers."],
            ImportSources = ["leadership.docx"]
        });
        second.Skills.Add(new Skill
        { Name = "kubernetes", EndorsementCount = 9, ImportSources = ["leadership.docx"] });
        second.Skills.Add(new Skill { Name = "Engineering Leadership" });
        second.Projects.Add(new Project
        {
            Name = "Atlas",
            Description = "Built and operated the platform for production customers.",
            Technologies = ["Azure"],
            Url = "https://example.com/atlas",
            ImportSources = ["leadership.docx"]
        });

        var state = new AppState();
        state.AddOrReplaceResume(first);
        state.AddOrReplaceResume(second);

        Assert.Equal(2, state.Resumes.Count);
        Assert.Equal(second.ResumeId, state.SelectedResumeId);

        var aggregate = Assert.IsType<ResumeDocument>(state.BuildAggregateResume());
        Assert.Single(aggregate.Experience);
        Assert.Equal(2, aggregate.Experience[0].Achievements.Count);
        Assert.Equal(2, aggregate.Experience[0].ImportSources.Count);
        Assert.Equal(2, aggregate.Skills.Count);
        var kubernetes = Assert.Single(aggregate.Skills, skill =>
            skill.Name.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(9, kubernetes.EndorsementCount);
        Assert.Equal(2, kubernetes.ImportSources.Count);
        var project = Assert.Single(aggregate.Projects);
        Assert.Equal(2, project.Technologies.Count);
        Assert.Equal(2, project.ImportSources.Count);
        Assert.Equal("https://example.com/atlas", project.Url);
        Assert.NotEmpty(aggregate.EvidenceLedger.Claims);
    }

    [Fact]
    public void AppState_AppliesReviewedPersonalOverridesToCompilerAggregate()
    {
        var imported = ResumeDocument.Create("old.docx", "application/docx", 100);
        imported.Personal.Email = "old@example.com";
        imported.Personal.Phone = "+44 0000 000000";
        var state = new AppState();
        state.AddOrReplaceResume(imported);
        state.Overrides.PersonalInfoOverrides[nameof(PersonalInfo.Email)] = "reviewed@example.com";
        state.Overrides.PersonalInfoOverrides[nameof(PersonalInfo.Phone)] = "+44 1111 111111";
        state.Overrides.PersonalInfoOverrides["Website"] = "https://example.com";

        var aggregate = Assert.IsType<ResumeDocument>(state.BuildAggregateResume());

        Assert.Equal("reviewed@example.com", aggregate.Personal.Email);
        Assert.Equal("+44 1111 111111", aggregate.Personal.Phone);
        Assert.Equal("https://example.com", aggregate.Personal.WebsiteUrl);
    }

    [Fact]
    public void AppState_AppliesReviewedRoleDatesWithoutChangingImportedEvidence()
    {
        var imported = ResumeDocument.Create("old.docx", "application/docx", 100);
        var role = new WorkExperience
        {
            Company = "ZenChef Limited",
            Title = "Lead Contract Developer",
            StartDate = new DateOnly(2024, 10, 1),
            IsCurrent = true
        };
        imported.Experience.Add(role);
        var state = new AppState();
        state.AddOrReplaceResume(imported);
        state.Overrides.ExperienceOverrides.Add(new lucidRESUME.Core.Models.Profile.ExperienceOverride
        {
            ExperienceId = role.Id,
            MatchRoleKey = AppState.CareerAnchorRoleKey(role),
            Company = "ZenChef Ltd / Formitable",
            Title = "Lead Contract Developer",
            StartDate = new DateOnly(2024, 10, 1),
            EndDate = new DateOnly(2026, 5, 1),
            IsCurrent = false
        });

        var aggregate = Assert.IsType<ResumeDocument>(state.BuildAggregateResume());

        var reviewed = Assert.Single(aggregate.Experience);
        Assert.Equal("ZenChef Ltd / Formitable", reviewed.Company);
        Assert.Equal(new DateOnly(2026, 5, 1), reviewed.EndDate);
        Assert.False(reviewed.IsCurrent);
        Assert.Single(state.Overrides.ExperienceOverrides);
        Assert.True(role.IsCurrent);
        Assert.Null(role.EndDate);
    }

}
