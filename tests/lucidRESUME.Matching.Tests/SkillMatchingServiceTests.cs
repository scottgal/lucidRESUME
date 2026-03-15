using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class SkillMatchingServiceTests
{
    private readonly SkillMatchingService _service = new();

    [Fact]
    public async Task Match_HighOverlap_ReturnsHighScore()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.Skills.Add(new Skill { Name = ".NET" });
        resume.Skills.Add(new Skill { Name = "Azure" });
        resume.Skills.Add(new Skill { Name = "SQL Server" });

        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET", "Azure", "SQL Server"];

        var profile = new UserProfile();
        var result = await _service.MatchAsync(resume, job, profile);

        Assert.True(result.Score >= 0.8);
        Assert.Equal(3, result.MatchedSkills.Count);
        Assert.Empty(result.MissingSkills);
    }

    [Fact]
    public async Task Match_BlockedCompany_ReturnsZero()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var job = JobDescription.Create("Role at Amazon", new JobSource { Type = JobSourceType.PastedText });
        job.Company = "Amazon";

        var profile = new UserProfile();
        profile.BlockCompany("Amazon");

        var result = await _service.MatchAsync(resume, job, profile);
        Assert.Equal(0, result.Score);
        Assert.Contains("blocked", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Match_NoRequiredSkills_ReturnsMidScore()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var job = JobDescription.Create("Generalist role", new JobSource { Type = JobSourceType.PastedText });
        var profile = new UserProfile();
        var result = await _service.MatchAsync(resume, job, profile);
        Assert.Equal(0.5, result.Score);
    }
}
