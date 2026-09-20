using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;

namespace lucidRESUME.Core.Tests.Parsing;

public class MarkdownSectionParserTests
{
    [Fact]
    public void PopulateSections_StripsStableAnchorFromCandidateName()
    {
        var resume = ResumeDocument.Create("resume.md", "text/markdown", 0);

        MarkdownSectionParser.PopulateSections(resume,
            "# Jane Smith {#jane-smith}\n\n## Summary {#summary}\n\nPlatform engineer.");

        Assert.Equal("Jane Smith", resume.Personal.FullName);
    }

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

    [Fact]
    public void PopulateSections_ParsesPortableContactTitleFirstRoleAndEducation()
    {
        const string markdown = """
            # Jane Smith
            Edinburgh, Scotland, UK | jane@example.com | linkedin.com/in/jane | github.com/jane | jane.dev

            ## Profile
            Platform engineer.

            ## Experience
            ### Lead Developer | Example Ltd | Jan 2022–Present {#lead-developer-example}
            - Built a platform used in production.

            ## Education
            **BSc (Hons) Psychology** | University of Stirling | Sep 1992–Jun 1996
            """;
        var resume = ResumeDocument.Create("portable.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("jane@example.com", resume.Personal.Email);
        Assert.Equal("Edinburgh, Scotland, UK", resume.Personal.Location);
        Assert.Equal("linkedin.com/in/jane", resume.Personal.LinkedInUrl);
        Assert.Equal("github.com/jane", resume.Personal.GitHubUrl);
        Assert.Equal("jane.dev", resume.Personal.WebsiteUrl);
        var experience = Assert.Single(resume.Experience);
        Assert.Equal("Lead Developer", experience.Title);
        Assert.Equal("Example Ltd", experience.Company);
        Assert.Null(experience.Location);
        var education = Assert.Single(resume.Education);
        Assert.Equal("BSc (Hons) Psychology", education.Degree);
        Assert.Equal("University of Stirling", education.Institution);
        Assert.Equal(1992, education.StartDate?.Year);
        Assert.Equal(1996, education.EndDate?.Year);
    }

    [Fact]
    public void PopulateSections_RejectsIncoherentTemplateHintsAndRecoversCompactCareer()
    {
        const string markdown = """
            # Scott Galloway

            Skills
            ### Technical
            ASP.NET Core / .NET Core - 5 years
            Azure / AWS - 5 years
            Distributed Systems Architecture (Cloud / Self-Hosted - 20 years
            ### Other
            Team Lead / Development Manager - 15 years+
            Development team recruitment / problem solving - 8 years

            ## Work
            ### Consulting - Lead Developer / Architect / Managing Director - 01/2012 - Present
            Recruited senior developers and acted as CTO for proposals and VC pitches.
            Huozhi Limited- Technical Consultant / Dev Lead - June 2018 - December 2019.
            Delivered a fintech modernisation programme.
            ### Microsoft Corp. - 01/2007-10/2009
            Program Manager (II) ASP.NET Team
            Led release governance and bug triage.
            ### StormID - Lead Developer - 02/2003 - 06/2005
            Delivered high-volume web applications.
            """;
        var misleadingTemplateSections = new List<DocumentSection>
        {
            new()
            {
                Heading = "Work",
                SemanticType = "Experience",
                Body = "02/1998 - 02/1999\n" + markdown,
                Level = 2
            }
        };
        var resume = ResumeDocument.Create("simple.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown, misleadingTemplateSections);

        Assert.True(resume.Experience.Count >= 4, $"Recovered only {resume.Experience.Count} roles");
        Assert.Contains(resume.Experience, role => role.Company?.Contains("Consulting") == true &&
                                                   role.Title?.Contains("Lead Developer") == true);
        Assert.Contains(resume.Experience, role => role.Company?.Contains("Microsoft") == true &&
                                                   role.Title?.Contains("Program Manager") == true);
        Assert.Contains(resume.Skills, skill => skill.Name == "ASP.NET Core");
        Assert.Contains(resume.Skills, skill => skill.Name == "Distributed Systems Architecture");
        Assert.Contains(resume.Skills, skill => skill.Name == "Development team recruitment");
    }
}
