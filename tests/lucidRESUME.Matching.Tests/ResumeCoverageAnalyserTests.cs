using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class ResumeCoverageAnalyserTests
{
    private static ResumeCoverageAnalyser CreateSut() =>
        new(new CompanyClassifier());

    private static ResumeDocument ResumeWith(params string[] skills)
    {
        var r = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        foreach (var s in skills)
            r.Skills.Add(new Skill { Name = s });
        return r;
    }

    private static JobDescription JobWith(string[] required, string[] preferred)
    {
        var j = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        j.RequiredSkills.AddRange(required);
        j.PreferredSkills.AddRange(preferred);
        return j;
    }

    [Fact]
    public async Task RequiredSkill_PresentInResume_IsCovered()
    {
        var resume = ResumeWith("C#", "Azure");
        var job = JobWith(["C#"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries, e => e.Requirement.Text == "C#");
        Assert.True(entry.IsCovered);
        Assert.Equal(RequirementPriority.Required, entry.Requirement.Priority);
    }

    [Fact]
    public async Task RequiredSkill_MissingFromResume_IsGap()
    {
        var resume = ResumeWith("Python");
        var job = JobWith(["Kubernetes"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Single(report.RequiredGaps);
        Assert.Equal(0, report.CoveragePercent);
    }

    [Fact]
    public async Task PreferredSkill_PresentInResume_IsCoveredWithPreferredPriority()
    {
        var resume = ResumeWith("Terraform");
        var job = JobWith([], ["Terraform"]);

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries);
        Assert.True(entry.IsCovered);
        Assert.Equal(RequirementPriority.Preferred, entry.Requirement.Priority);
    }

    [Fact]
    public async Task Responsibility_MatchedByKeyword_IsCovered()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var exp = new WorkExperience { Company = "Acme" };
        exp.Achievements.Add("Designed and built a CI/CD pipeline reducing deploy time by 40%");
        resume.Experience.Add(exp);

        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        job.Responsibilities.Add("Build and maintain CI/CD pipelines");

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries, e => e.Requirement.Priority == RequirementPriority.Responsibility);
        Assert.True(entry.IsCovered);
    }

    [Fact]
    public async Task CoveragePercent_PartialMatch_IsCorrect()
    {
        var resume = ResumeWith("C#");
        var job = JobWith(["C#", "Kubernetes"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Equal(50, report.CoveragePercent);
    }

    [Fact]
    public async Task CompanyType_StartupSignal_ClassifiedCorrectly()
    {
        var resume = ResumeWith("React");
        var job = JobDescription.Create("Series A startup building fintech", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills.Add("React");

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Equal(CompanyType.Startup, report.CompanyType);
    }

    [Fact]
    public async Task SkillInExperienceTechnologies_CountsAsEvidence()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var exp = new WorkExperience { Company = "TechCo" };
        exp.Technologies.Add("GraphQL");
        resume.Experience.Add(exp);

        var job = JobWith(["GraphQL"], []);
        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.True(report.Entries.Single().IsCovered);
    }
}
