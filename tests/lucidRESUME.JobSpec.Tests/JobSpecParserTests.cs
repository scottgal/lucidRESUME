using lucidRESUME.JobSpec;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucidRESUME.JobSpec.Tests;

public class JobSpecParserTests
{
    // Use the internal text-only constructor - no scraper needed for these tests
    private readonly JobSpecParser _parser = new(NullLogger<JobSpecParser>.Instance);

    [Fact]
    public async Task Competency_outline_does_not_discard_extracted_named_skill_mentions()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "morena-head-applied-ai.txt"));
        var job = await _parser.ParseFromTextAsync(text + "\nRequired skills\n- C#\n- PostgreSQL\n");
        Assert.NotEmpty(job.Competencies);
        Assert.Contains("C#", job.MentionedSkills);
        Assert.Contains("PostgreSQL", job.MentionedSkills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Morena_advert_preserves_competency_groups_and_separates_optional_experience(bool withLinkedInChrome)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "morena-head-applied-ai.txt"));
        if (withLinkedInChrome) text = "Company logo for, Morena.\nMorena\n\n" + text;
        var job = await _parser.ParseFromTextAsync(text);
        Assert.Equal("Head of Applied AI Engineering", job.Title);
        Assert.Equal("Morena", job.Company);
        Assert.Equal(7, job.RequiredSkills.Count);
        Assert.Equal(7, job.Responsibilities.Count);
        Assert.Equal(10, job.PreferredSkills.Count);
        Assert.Contains(job.PreferredSkills, skill => skill.StartsWith("Ideally the system"));
        Assert.Contains("Production agent experience", job.RequiredSkills);
        Assert.Contains("Evaluation experience", job.RequiredSkills);
        Assert.Contains("Safe agent actions", job.RequiredSkills);
        Assert.Contains("Model judgment", job.RequiredSkills);
        Assert.Contains(job.PreferredSkills, skill => skill.Contains("Fine-tuning"));
        Assert.DoesNotContain(job.RequiredSkills, skill => skill.Contains("Compensation") || skill.Contains("equity"));
        var safety = Assert.Single(job.Competencies, competency => competency.Name == "Safety and reliability");
        Assert.Contains("idempotency", safety.Description);
        Assert.Contains("human-in-the-loop", safety.Description);
        Assert.Equal(text, job.RawText);
    }

    [Theory]
    [InlineData("file:///private/etc/passwd")]
    [InlineData("ftp://example.com/job.txt")]
    public async Task ParseFromUrl_RejectsNonHttpSchemes(string url)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => _parser.ParseFromUrlAsync(url));
        Assert.Contains("http or https", error.Message);
    }

    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/job")]
    [InlineData("http://192.168.1.10/job")]
    public async Task ParseFromUrl_RejectsLocalNetworkTargets(string url)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => _parser.ParseFromUrlAsync(url));
        Assert.Contains("local or private", error.Message);
    }

    [Fact]
    public async Task ParseFromText_ExtractsTitleAndCompany()
    {
        var text = """
            Senior Software Engineer at Acme Corp
            We are looking for an experienced developer...
            Required: 5+ years of C# experience
            Skills: .NET, Azure, SQL Server
            Salary: £60,000 - £80,000 per year
            Remote: Yes
            """;

        var job = await _parser.ParseFromTextAsync(text);

        Assert.Equal("Senior Software Engineer", job.Title);
        Assert.Equal("Acme Corp", job.Company);
        Assert.Contains(".NET", job.RequiredSkills);
        Assert.True(job.IsRemote);
    }

    [Fact]
    public async Task ParseFromText_ExtractsThreeLineAdvertHeader()
    {
        var text = """
            Engineering Technical Lead
            Planet DDS
            Glasgow, Scotland, United Kingdom. Hybrid.

            Required skills
            - .NET
            - DDS
            """;

        var job = await _parser.ParseFromTextAsync(text);

        Assert.Equal("Engineering Technical Lead", job.Title);
        Assert.Equal("Planet DDS", job.Company);
        Assert.DoesNotContain(job.RequiredSkills, skill =>
            string.Equals(skill, "DDS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseFromText_ExtractsTitleFromFlattenedAdvertWithoutOptionalModels()
    {
        const string text =
            "Head of Engineering. TypeScript and AWS experience required. Lead engineering change.";

        var job = await _parser.ParseFromTextAsync(text);

        Assert.Equal("Head of Engineering", job.Title);
    }

    [Fact]
    public async Task ParseFromText_ExtractsSalary()
    {
        var text = "Salary: £60,000 - £80,000 per annum. Location: London.";
        var job = await _parser.ParseFromTextAsync(text);
        Assert.NotNull(job.Salary);
        Assert.Equal(60000, job.Salary!.Min);
        Assert.Equal(80000, job.Salary.Max);
    }
}
