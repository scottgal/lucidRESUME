using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class ResumeGenerationBudgetTests
{
    [Fact]
    public void Automatic_scope_grows_with_distinct_requirements_not_repeated_advert_text()
    {
        var simple = new JobDescription { RawText = "Build reliable APIs." };
        var duplicate = new JobDescription { RawText = string.Join('\n', Enumerable.Repeat(simple.RawText, 100)) };
        var complex = new JobDescription
        {
            Responsibilities = Enumerable.Range(1, 30).Select(i => $"Own platform capability {i}").ToList()
        };
        var options = new ResumeGenerationOptions();
        var baseline = ResumeGenerationBudget.Resolve(simple, options);
        Assert.Equal(baseline, ResumeGenerationBudget.Resolve(duplicate, options));
        var expanded = ResumeGenerationBudget.Resolve(complex, options);
        Assert.True(expanded.Roles > baseline.Roles);
        Assert.True(expanded.AchievementsPerRole > baseline.AchievementsPerRole);
        Assert.True(expanded.Projects > baseline.Projects);
    }

    [Fact]
    public void Detail_and_expression_are_independent_of_role_scope()
    {
        var job = new JobDescription();
        var extended = new ResumeGenerationOptions { Length = ResumeLength.Extended };
        var direct = ResumeGenerationBudget.Resolve(job, extended);
        Assert.Equal(direct, ResumeGenerationBudget.Resolve(job, extended with { Tone = ResumeTone.Expressive }));
        var thorough = ResumeGenerationBudget.Resolve(job, extended with { Detail = ResumeDetail.Thorough });
        Assert.Equal(direct.Roles, thorough.Roles);
        Assert.True(thorough.AchievementsPerRole > direct.AchievementsPerRole);
        Assert.True(direct.Roles > ResumeGenerationBudget.Resolve(job, new() { Length = ResumeLength.Concise }).Roles);
    }
}
