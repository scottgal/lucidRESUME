using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class CompanyClassifierTests
{
    private readonly CompanyClassifier _sut = new();

    [Theory]
    [InlineData("We are a Series B startup disrupting the lending space", CompanyType.Startup)]
    [InlineData("Join our scale-up as we grow from 50 to 500", CompanyType.ScaleUp)]
    [InlineData("Fortune 500 enterprise software company", CompanyType.Enterprise)]
    [InlineData("We are a digital agency delivering for top brands", CompanyType.Agency)]
    [InlineData("Big 4 consultancy offering advisory services", CompanyType.Consultancy)]
    [InlineData("Global investment bank, regulated environment", CompanyType.Finance)]
    [InlineData("NHS Trust providing healthcare services", CompanyType.Public)]
    [InlineData("Research university seeks lecturer", CompanyType.Academic)]
    [InlineData("Some company doing some things", CompanyType.Unknown)]
    public void Classify_DetectsExpectedType(string rawText, CompanyType expected)
    {
        var job = JobDescription.Create(rawText, new JobSource { Type = JobSourceType.PastedText });
        Assert.Equal(expected, _sut.Classify(job));
    }

    [Fact]
    public void Classify_TitleSignals_Startup()
    {
        var job = JobDescription.Create("salary £80k", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Senior Engineer at a seed-stage startup";
        Assert.Equal(CompanyType.Startup, _sut.Classify(job));
    }
}
