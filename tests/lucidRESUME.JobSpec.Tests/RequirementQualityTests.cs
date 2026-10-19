using lucidRESUME.JobSpec.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucidRESUME.JobSpec.Tests;

public sealed class RequirementQualityTests
{
    [Fact]
    public async Task Flattened_remote_header_does_not_turn_the_advert_body_into_an_employer()
    {
        var job = await new JobSpecParser(NullLogger<JobSpecParser>.Instance).ParseFromTextAsync(
            "Senior .NET Developer at Remote. We need C# and SQL Server experience.");
        Assert.Equal("Senior .NET Developer", job.Title);
        Assert.Null(job.Company);
        Assert.True(job.IsRemote);
    }

    [Fact]
    public async Task Chrome_headings_and_optional_tenure_do_not_become_role_identity_or_mandatory_experience()
    {
        var job = await new JobSpecParser(NullLogger<JobSpecParser>.Instance).ParseFromTextAsync(
            "About the job\nWe are looking for a Development Team Lead to lead four engineers.\nWho You Are\nC# and SQL Server.\nPreferably 10+ years of engineering experience.\nYou don't need to have worked with Kubernetes.\nNice to Have\nDocker.");
        Assert.Equal("Development Team Lead", job.Title);
        Assert.Null(job.RequiredYearsExperience);
        Assert.Contains("Docker", job.PreferredSkills);
        Assert.DoesNotContain("Docker.", job.PreferredSkills);
        Assert.DoesNotContain(job.RequiredSkills, text => text.Contains("don't need") || text.StartsWith("Preferably"));
    }

    [Fact]
    public async Task Flattened_sections_do_not_create_comma_fragments_or_promote_optional_tools()
    {
        const string text = """
            Development Team Lead at Example
            What You'll Be Doing
            Hands-On Engineering Build production services. Own architecture decisions. Coach engineers.
            Product Integrations Build resilient APIs. Add monitoring and retries. Own operation.
            What we need:
            We are looking for 7–10 years of professional development experience. You don't need to have worked in FinTech. You should enjoy mentoring developers.
            Essential 7–10 years of professional software development experience, with strong, current hands-on skills in .NET / C#. Solid experience with SQL Server: queries, stored procedures, and relational data modelling. Proven experience designing, building, and consuming RESTful APIs and integrating third-party systems.
            Nice to Have Experience with Docker and Kubernetes. Familiarity with Azure DevOps. Experience working in FinTech.
            Benefits
            Pension, private medical insurance and training.
            """;
        var job = await new JobSpecParser(NullLogger<JobSpecParser>.Instance).ParseFromTextAsync(text);
        Assert.Empty(job.Competencies);
        Assert.Contains("C#", job.RequiredSkills);
        Assert.Contains("SQL Server", job.RequiredSkills);
        Assert.DoesNotContain("with strong", job.RequiredSkills);
        Assert.DoesNotContain("SOLID", job.RequiredSkills);
        Assert.DoesNotContain("FinTech", job.RequiredSkills);
        Assert.Equal("preferred", JobRequirementContext.Priority(text, "FinTech"));
        Assert.Contains("Docker", job.PreferredSkills);
        var docker = Assert.Single(job.SkillDecisions, item => item.Label == "Docker");
        Assert.Equal("preferred", docker.SourceSection);
        Assert.Contains("Docker", docker.SourceText);
    }

    [Fact]
    public void Structural_competency_sentences_keep_their_commas()
    {
        var candidates = StructuralExtractor.Extract("Engineer at Example\nRequirements\nExperience designing APIs, integrations, and resilient services.");
        Assert.Contains(candidates, item => item.FieldType == "skill" && item.Value == "Experience designing APIs, integrations, and resilient services.");
        Assert.DoesNotContain(candidates, item => item.Value == "integrations");
    }

    [Fact]
    public async Task Context_retains_requirement_priority_and_excludes_benefits_and_ordinary_words()
    {
        const string text = """
            Development Team Lead
            Company: Example
            Essential
            C# and SQL Server, strong system design. Solid experience delivering safe deployments.
            Nice to Have Experience with Docker and Kubernetes.
            Benefits
            Pension, training, wellbeing, private medical insurance and LinkedIn Learning.
            """;
        JdFieldCandidate[] candidates = [new("skill", "C#", .8, "taxonomy"), new("skill", "SQL Server", .8, "taxonomy"),
            new("skill", "System design", .8, "taxonomy"), new("skill", "Docker", .8, "taxonomy"), new("skill", "Kubernetes", .8, "taxonomy"),
            new("skill", "SOLID", .8, "taxonomy"), new("skill", "SAFe", .8, "taxonomy"), new("skill", "Pension", .8, "taxonomy"),
            new("skill", "LinkedIn Learning", .8, "taxonomy"), new("skill", "Development", .8, "taxonomy")];
        var classified = await new JobSkillClassifier().ClassifyAsync(text, candidates, default);
        Assert.Equal(new[] { "C#", "SQL Server", "System design", "Docker", "Kubernetes" }, classified.Candidates.Select(item => item.Value));
        Assert.Equal("preferred", JobRequirementContext.Priority(text, "Docker"));
        Assert.Equal("ignore", JobRequirementContext.Priority(text, "Pension"));
    }

    [Fact]
    public async Task Real_acronyms_and_repeated_required_mentions_are_not_discarded()
    {
        const string text = "Essential\nC#, SOLID principles and SAFe certification. Docker.\nBenefits\nDocker training budget.";
        var result = await new JobSkillClassifier().ClassifyAsync(text,
            [new("skill", "SOLID", .9, "taxonomy"), new("skill", "SAFe", .9, "taxonomy"), new("skill", "Docker", .9, "taxonomy")], default);
        Assert.Equal(3, result.Candidates.Count);
        Assert.Equal("required", JobRequirementContext.Priority(text, "Docker"));
    }

    [Fact]
    public async Task Plain_preferred_heading_does_not_make_docker_mandatory()
    {
        var job = await new JobSpecParser(NullLogger<JobSpecParser>.Instance).ParseFromTextAsync(
            "Engineer at Example\nRequired skills\n- C#\nNice to have\n- Docker\nBenefits\n- Pension");
        Assert.Contains("C#", job.RequiredSkills);
        Assert.Contains("Docker", job.PreferredSkills);
        Assert.DoesNotContain("Docker", job.RequiredSkills);
        Assert.DoesNotContain("Pension", job.RequiredSkills.Concat(job.PreferredSkills));
    }
}
