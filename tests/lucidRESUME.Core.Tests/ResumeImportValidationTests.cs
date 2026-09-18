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
            Achievements = ["Led the platform migration."]
        });
        first.Skills.Add(new Skill { Name = "Kubernetes" });

        var second = ResumeDocument.Create("leadership.docx", "application/docx", 100);
        second.Experience.Add(new WorkExperience
        {
            Company = "Northstar Systems Ltd",
            Title = "Principal Platform Engineer",
            StartDate = new DateOnly(2022, 2, 1),
            IsCurrent = true,
            Achievements = ["Led the platform migration.", "Mentored eight engineers."]
        });
        second.Skills.Add(new Skill { Name = "kubernetes" });
        second.Skills.Add(new Skill { Name = "Engineering Leadership" });

        var state = new AppState();
        state.AddOrReplaceResume(first);
        state.AddOrReplaceResume(second);

        Assert.Equal(2, state.Resumes.Count);
        Assert.Equal(second.ResumeId, state.SelectedResumeId);

        var aggregate = Assert.IsType<ResumeDocument>(state.BuildAggregateResume());
        Assert.Single(aggregate.Experience);
        Assert.Equal(2, aggregate.Experience[0].Achievements.Count);
        Assert.Equal(2, aggregate.Skills.Count);
    }

}
