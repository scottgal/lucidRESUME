using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;

namespace lucidRESUME.Core.Tests.Parsing;

public class MarkdownSectionParserTests
{
    // Minimal resume markdown matching the Docling output format
    private const string SampleMarkdown = """
        ## Scott Galloway | .NET Developer | Remote

        I'm a senior .NET developer and technical leader.

        ## Skills

        ## Languages & Frameworks

        Server Side: C#, Python, Java Frontend: Vue.js, React

        ## Databases

        SQL: SQL Server, PostgreSQL NoSQL: MongoDB

        ## Employment History mostlylucid limited | Freelance Consultant / Lead Developer | Remote

        ## Jan 2012 -Present

        Delivered high-impact projects for diverse clients.

        ## ZenChef Limited | Lead Contract Developer | Remote Oct 2024 -Present

        Integrated delivery-focused strategies for a large legacy system.

        ## Education

        University of Stirling | BSc (Hons) Psychology
        """;

    [Fact]
    public void PopulateSections_ExtractsFullName()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.Equal("Scott Galloway", resume.Personal.FullName);
    }

    [Fact]
    public void PopulateSections_ExtractsSummary()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.NotNull(resume.Personal.Summary);
        Assert.Contains("senior .NET developer", resume.Personal.Summary);
    }

    [Fact]
    public void PopulateSections_ExtractsSkills()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.True(resume.Skills.Count > 0);
        Assert.Contains(resume.Skills, s => s.Name == "C#");
        Assert.Contains(resume.Skills, s => s.Name.Contains("PostgreSQL"));
    }

    [Fact]
    public void PopulateSections_SkillsHaveCategories()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        var csharp = resume.Skills.FirstOrDefault(s => s.Name == "C#");
        Assert.NotNull(csharp);
        Assert.Equal("Server Side", csharp.Category);
    }

    [Fact]
    public void PopulateSections_ExtractsExperienceViaPipeHeadingFallback()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.True(resume.Experience.Count > 0);
        Assert.Contains(resume.Experience, e => e.Company?.Contains("ZenChef") == true);
    }

    [Fact]
    public void PopulateSections_ExtractsDates()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        var zenchef = resume.Experience.FirstOrDefault(e => e.Company?.Contains("ZenChef") == true);
        Assert.NotNull(zenchef);
        Assert.Equal(2024, zenchef.StartDate?.Year);
        Assert.True(zenchef.IsCurrent);
    }

    [Fact]
    public void PopulateSections_ExtractsEducation()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.True(resume.Education.Count > 0);
        var edu = resume.Education[0];
        Assert.Contains("Stirling", edu.Institution);
    }

    [Fact]
    public void PopulateSections_DoesNotOverwriteExistingFullName()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        resume.Personal.FullName = "Already Set";
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        Assert.Equal("Already Set", resume.Personal.FullName);
    }

    [Fact]
    public void PopulateSections_DoesNotOverwriteExistingSkills()
    {
        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 0);
        resume.Skills.Add(new Skill { Name = "Existing" });
        MarkdownSectionParser.PopulateSections(resume, SampleMarkdown);

        // Skills should not be re-parsed when already populated
        Assert.Single(resume.Skills);
        Assert.Equal("Existing", resume.Skills[0].Name);
    }
}
