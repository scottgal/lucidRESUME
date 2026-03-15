using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class SkillMatchingServiceTests
{
    private readonly SkillMatchingService _service = new(new AspectExtractor());

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

    [Fact]
    public async Task Match_VoteAdjustment_RaisesScoreForPositivelyVotedAspects()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.Skills.Add(new Skill { Name = ".NET" });

        // Two required skills but resume only has one → base score = 0.5 (room to go up)
        var job = JobDescription.Create("Remote .NET role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET", "Azure"];
        job.IsRemote = true;

        var profile = new UserProfile();
        var baseResult = await _service.MatchAsync(resume, job, profile);

        // Upvote the Remote work model
        profile.VoteUp(AspectType.WorkModel, "Remote");
        profile.VoteUp(AspectType.WorkModel, "Remote"); // score = +2

        var votedResult = await _service.MatchAsync(resume, job, profile);

        Assert.True(votedResult.Score > baseResult.Score,
            $"Expected score to increase after upvoting Remote; was {baseResult.Score}, got {votedResult.Score}");
    }

    [Fact]
    public async Task Match_VoteAdjustment_ClampedAt_One()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.Skills.Add(new Skill { Name = ".NET" });

        var job = JobDescription.Create("Remote .NET role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET"];
        job.IsRemote = true;

        var profile = new UserProfile();
        // Vote up many times to try to push above 1.0
        for (int i = 0; i < 20; i++)
            profile.VoteUp(AspectType.WorkModel, "Remote");

        var result = await _service.MatchAsync(resume, job, profile);

        Assert.True(result.Score <= 1.0, $"Score {result.Score} exceeded maximum of 1.0");
    }

    [Fact]
    public async Task Match_VoteAdjustment_ClampedAt_Zero()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);

        var job = JobDescription.Create("Onsite role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["Python"]; // not in resume — base score = 0

        var profile = new UserProfile();
        // Downvote to try to push below 0.0
        for (int i = 0; i < 20; i++)
            profile.VoteDown(AspectType.WorkModel, "Onsite");

        var result = await _service.MatchAsync(resume, job, profile);

        Assert.True(result.Score >= 0.0, $"Score {result.Score} went below minimum of 0.0");
    }
}
