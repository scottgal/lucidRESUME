using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;

namespace lucidRESUME.Core.Tests.Parsing;

public class MarkdownSectionParserTests
{
    [Fact]
    public void Core_skill_prose_links_keep_plain_skill_names_in_structured_export()
    {
        const string markdown = """
            # Alex Example

            ## Core Skills

            **Platform engineering:** [TypeScript](#section-1-role), [AWS](#section-1-role)

            ## Experience

            ### Engineer {#section-1-role}

            Built a TypeScript service on AWS.
            """;
        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Contains(resume.Skills, skill => skill.Name == "TypeScript");
        Assert.Contains(resume.Skills, skill => skill.Name == "AWS");
        Assert.DoesNotContain(resume.Skills, skill => skill.Name.Contains("#section-", StringComparison.Ordinal));
    }

    [Fact]
    public void PopulateSections_Parses_middle_dot_contact_line_and_preference()
    {
        var resume = ResumeDocument.Create("resume.md", "text/markdown", 0);
        const string markdown = """
            # Scott Galloway

            Glasgow, United Kingdom · scott@mostlylucid.net · 07498 479614 (please email in the first instance)

            ## Summary

            Engineering leader.
            """;

        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("Glasgow, United Kingdom", resume.Personal.Location);
        Assert.Equal("scott@mostlylucid.net", resume.Personal.Email);
        Assert.Equal("07498 479614", resume.Personal.Phone);
        Assert.Equal("please email in the first instance", resume.Personal.ContactPreference);
    }

    [Fact]
    public void PopulateSections_Parses_exported_hyphenated_contact_preference()
    {
        var resume = ResumeDocument.Create("transcript.md", "text/markdown", 0);
        const string markdown = """
            # Alex Example

            alex@example.com | 07498 479614 - please email in the first instance - | Glasgow, United Kingdom

            ## Summary

            Engineering leader.
            """;

        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("07498 479614", resume.Personal.Phone);
        Assert.Equal("please email in the first instance", resume.Personal.ContactPreference);
    }

    [Fact]
    public void Career_anchor_directive_marks_role_without_becoming_achievement_text()
    {
        const string markdown = """
            # Jane Smith

            ## Experience

            ### Program Manager II | Microsoft Corp
            <!-- lucidresume:career-anchor -->
            *Jan 2007 - Oct 2009*

            - Shipped the first ASP.NET MVC release.
            """;
        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        var role = Assert.Single(resume.Experience);
        Assert.True(role.IsCareerAnchor);
        Assert.Equal("Shipped the first ASP.NET MVC release.", Assert.Single(role.Achievements));
    }

    [Fact]
    public void PopulateSections_StopsEducationAtUnknownPeerHeading()
    {
        const string markdown = """
                                # Avery Example

                                ## Education

                                ### BSc Computer Science | Example University

                                ## Selected Public Evidence

                                - https://example.test/article
                                """;
        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        var education = Assert.Single(resume.Education);
        Assert.Equal("BSc Computer Science", education.Degree);
        Assert.Equal("Example University", education.Institution);
    }

    [Fact]
    public void ParsesMarkdownProjectsWithExternalEvidenceAndTechnologies()
    {
        var resume = ResumeDocument.Create("projects.md", "text/markdown", 1);

        MarkdownSectionParser.PopulateSections(resume, """
            # Jane Smith

            ## Projects

            ### Atlas Retrieval

            https://github.com/example/atlas

            Built an evidence-grounded retrieval platform with deterministic citations.

            **Technologies:** C#, RAG, PostgreSQL
            """);

        var project = Assert.Single(resume.Projects);
        Assert.Equal("Atlas Retrieval", project.Name);
        Assert.Equal("https://github.com/example/atlas", project.Url?.TrimEnd('/'));
        Assert.Equal("Built an evidence-grounded retrieval platform with deterministic citations.", project.Description);
        Assert.Equal(["C#", "RAG", "PostgreSQL"], project.Technologies);
    }

    [Fact]
    public void Numeric_date_to_present_role_is_not_split_into_company_and_title()
    {
        var resume = ResumeDocument.Create("numeric-date.md", "text/markdown", 100);

        MarkdownSectionParser.PopulateSections(resume, """
            # Alex Example

            ## Work History

            20/01/2012 – Present Freelance Developer
            Built customer applications on AWS.
            """);

        var role = Assert.Single(resume.Experience);
        Assert.Equal("Freelance Developer", role.Title);
        Assert.Null(role.Company);
        Assert.Equal(new DateOnly(2012, 1, 20), role.StartDate);
        Assert.True(role.IsCurrent);
    }

    [Fact]
    public void Date_first_company_location_entries_take_title_from_following_line()
    {
        var resume = ResumeDocument.Create("legacy.docx", "application/docx", 100);

        MarkdownSectionParser.PopulateSections(resume, """
            # Alex Example

            ## Experience
            01/2007-10/2009 – Microsoft Corporation, Redmond
            Program Manager, ASP.NET Team
            Coordinated product releases across engineering teams.

            02/2003-06/2005 – Storm ID Ltd, Edinburgh, UK
            Senior Software Architect
            Built high-volume public web systems.
            """);

        Assert.Collection(resume.Experience,
            microsoft =>
            {
                Assert.Equal("Microsoft Corporation", microsoft.Company);
                Assert.Equal("Program Manager, ASP.NET Team", microsoft.Title);
                Assert.Equal("Redmond", microsoft.Location);
                Assert.Equal(new DateOnly(2007, 1, 1), microsoft.StartDate);
            },
            storm =>
            {
                Assert.Equal("Storm ID Ltd", storm.Company);
                Assert.Equal("Senior Software Architect", storm.Title);
                Assert.Equal("Edinburgh, UK", storm.Location);
            });
    }

    [Fact]
    public void Heuristic_parse_with_dates_beats_stale_template_section_without_dates()
    {
        const string markdown = """
            # Alex Example
            ## Experience
            #### ZenChef Limited | Lead Contract Developer | Remote
            ### Oct 2024 – Present
            Operated a distributed production platform.
            """;
        var sections = new List<DocumentSection>
        {
            new()
            {
                Heading = "Experience",
                Body = "#### ZenChef Limited | Lead Contract Developer | Remote",
                Level = 2,
                SemanticType = "Experience"
            },
            new()
            {
                Heading = "Oct 2024 – Present",
                Body = "Operated a distributed production platform.",
                Level = 3,
                SemanticType = "Experience"
            }
        };
        var resume = ResumeDocument.Create("template.docx", "application/docx", 100);

        MarkdownSectionParser.PopulateSections(resume, markdown, sections);

        var role = Assert.Single(resume.Experience);
        Assert.Equal("ZenChef Limited", role.Company);
        Assert.Equal(new DateOnly(2024, 10, 1), role.StartDate);
        Assert.True(role.IsCurrent);
    }

    [Fact]
    public void PopulateSections_StripsStableAnchorFromCandidateName()
    {
        var resume = ResumeDocument.Create("resume.md", "text/markdown", 0);

        MarkdownSectionParser.PopulateSections(resume,
            "# Jane Smith {#jane-smith}\n\n## Summary {#summary}\n\nPlatform engineer.");

        Assert.Equal("Jane Smith", resume.Personal.FullName);
    }

    [Fact]
    public void PopulateSections_StripsCompactCitationsFromParsedProjectionButKeepsEvidenceSectionsSeparate()
    {
        const string markdown = """
            # Jane Smith

            ## Summary {#summary}
            Platform engineer. [[1]](#ref-1)

            ## Experience {#experience}
            ### Platform Engineer - Example Corp {#experience-example}
            *Jan 2022 – Present*
            - Built a reliable platform. [[2]](#ref-2)

            ## References
            cJobML 0.1: xref [n] in prose resolves to ref [n].
            <a id="ref-1"></a>[1] “Complete transcript: Professional summary.” [Career Transcript]
            <a id="ref-2"></a>[2] “Complete transcript: Platform Engineer · Example Corp.” [Career Transcript]
            """;
        var resume = ResumeDocument.Create("projection.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        Assert.Equal("Platform engineer.", resume.Personal.Summary);
        var experience = Assert.Single(resume.Experience);
        Assert.Equal("Platform Engineer", experience.Title);
        Assert.Equal("Example Corp", experience.Company);
        Assert.Equal("Built a reliable platform.", Assert.Single(experience.Achievements));
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
            Edinburgh, Scotland, UK | jane@example.com | +44 7700 900123 | linkedin.com/in/jane | github.com/jane | jane.dev

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
        Assert.Equal("+44 7700 900123", resume.Personal.Phone);
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
    public void PopulateSections_PreservesCompactRoleDirective()
    {
        const string markdown = """
            # Jane Smith

            ## Experience
            ### Lead Developer - Example Ltd
            <!-- lucidresume:compact -->
            *Jan 2020 - Dec 2021*

            - Maintained the production platform.
            """;
        var resume = ResumeDocument.Create("portable.md", "text/markdown", markdown.Length);

        MarkdownSectionParser.PopulateSections(resume, markdown);

        var experience = Assert.Single(resume.Experience);
        Assert.True(experience.IsCompact);
        Assert.Equal("Lead Developer", experience.Title);
        Assert.Equal("Example Ltd", experience.Company);
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
