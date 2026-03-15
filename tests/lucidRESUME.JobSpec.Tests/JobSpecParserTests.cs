using lucidRESUME.JobSpec;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucidRESUME.JobSpec.Tests;

public class JobSpecParserTests
{
    // Use the internal text-only constructor — no scraper needed for these tests
    private readonly JobSpecParser _parser = new(NullLogger<JobSpecParser>.Instance);

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
    public async Task ParseFromText_ExtractsSalary()
    {
        var text = "Salary: £60,000 - £80,000 per annum. Location: London.";
        var job = await _parser.ParseFromTextAsync(text);
        Assert.NotNull(job.Salary);
        Assert.Equal(60000, job.Salary!.Min);
        Assert.Equal(80000, job.Salary.Max);
    }
}
