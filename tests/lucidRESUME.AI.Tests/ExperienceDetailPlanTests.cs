using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class ExperienceDetailPlanTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static WorkExperience Role(int start, int end, int facts = 8) => new()
    {
        Company = "Company",
        Title = "Engineer",
        StartDate = new(start, 1, 1),
        EndDate = new(end, 1, 1),
        Achievements = Enumerable.Range(0, facts).Select(i => $"Supported account {i}").ToList()
    };

    [Fact]
    public void Ten_year_requirement_extends_chronology_beyond_six_compact_roles()
    {
        var roles = Enumerable.Range(2016, 10).Select(year => Role(year, year + 1)).ToList();
        var detailed = new HashSet<Guid> { roles[^1].Id };
        var plan = ExperienceDetailPlan.Build(new() { RequiredYearsExperience = 10 }, roles, detailed,
            new Dictionary<Guid, double>(), new Dictionary<Guid, IReadOnlySet<string>>(), new(1, 3, 0, 80, 0), Today);
        Assert.Equal(9, plan.ChronologyRoleIds.Count);
        Assert.InRange(plan.EvidencedYears, 9.99, 10.01);
    }

    [Fact]
    public void Concurrent_roles_and_unknown_dates_do_not_invent_tenure()
    {
        var a = Role(2020, 2026); var b = Role(2020, 2026); var unknown = new WorkExperience();
        Assert.InRange(ExperienceDetailPlan.CoveredYears([a, b, unknown], Today), 5.99, 6.01);
        var gap = Role(2010, 2012);
        Assert.InRange(ExperienceDetailPlan.CoveredYears([a, gap], Today), 7.99, 8.01);
    }

    [Fact]
    public void Recent_relevant_role_receives_more_detail_within_shared_budget()
    {
        var old = Role(2000, 2010); var recent = Role(2020, 2026);
        var plan = ExperienceDetailPlan.Build(new(), [old, recent], new HashSet<Guid> { old.Id, recent.Id },
            new Dictionary<Guid, double> { [recent.Id] = 24 }, new Dictionary<Guid, IReadOnlySet<string>>(),
            new(2, 3, 0, 80, 0), Today);
        Assert.True(plan.AchievementLimits[recent.Id] > plan.AchievementLimits[old.Id]);
        Assert.True(plan.AchievementLimits.Values.Sum() <= 6);
        Assert.All(plan.AchievementLimits.Values, limit => Assert.True(limit >= 1));
    }

    [Fact]
    public void Older_role_with_unique_required_evidence_earns_more_space()
    {
        var old = Role(2000, 2010); var recent = Role(2020, 2026);
        var requirements = new Dictionary<Guid, IReadOnlySet<string>> { [old.Id] = new HashSet<string> { "safety", "evaluation", "leadership" } };
        var plan = ExperienceDetailPlan.Build(new(), [old, recent], new HashSet<Guid> { old.Id, recent.Id },
            new Dictionary<Guid, double>(), requirements, new(2, 3, 0, 80, 0), Today);
        Assert.True(plan.AchievementLimits[old.Id] > plan.AchievementLimits[recent.Id]);
    }

    [Fact]
    public void Role_selection_rewards_evidence_missing_from_recent_roles()
    {
        var recent = Role(2024, 2026); var similar = Role(2022, 2024); var older = Role(2010, 2015);
        var selected = SemanticCompressor.SelectDetailedRoleIds([recent, similar, older],
            new Dictionary<Guid, double> { [recent.Id] = 10, [similar.Id] = 9 }, maximumRelevantRoles: 2,
            requirementHints: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [recent.Id] = new HashSet<string> { "api" },
                [similar.Id] = new HashSet<string> { "api" },
                [older.Id] = new HashSet<string> { "safety", "recovery" }
            });
        Assert.Contains(older.Id, selected);
        Assert.Contains(recent.Id, selected);
        Assert.DoesNotContain(similar.Id, selected);
    }

    [Fact]
    public void Month_only_end_dates_cover_the_declared_month_without_changing_exact_day_dates()
    {
        var roles = Enumerable.Range(2016, 10).Select(year => new WorkExperience
        {
            StartDate = new(year, 1, 1),
            EndDate = new(year, 12, 1),
            EndDateIsMonthOnly = true
        }).ToList();
        Assert.InRange(ExperienceDetailPlan.CoveredYears(roles, Today), 9.99, 10.01);
        Assert.InRange(ExperienceDetailPlan.CoveredYears([Role(2020, 2026)], Today), 5.99, 6.01);
    }

    [Fact]
    public void Consecutive_closed_calendar_years_include_each_roles_last_day()
    {
        var roles = Enumerable.Range(2016, 10).Select(year => new WorkExperience
        {
            StartDate = new(year, 1, 1),
            EndDate = new(year, 12, 31)
        }).ToList();
        Assert.InRange(ExperienceDetailPlan.CoveredYears(roles, Today), 9.99, 10.01);
    }

    [Theory]
    [InlineData("At least 10+ years of professional software engineering experience", 10)]
    [InlineData("10 years of Kubernetes experience", null)]
    public void Raw_advert_fallback_distinguishes_career_from_skill_tenure(string text, int? expected)
    {
        var plan = ExperienceDetailPlan.Build(new() { RawText = text }, [], new HashSet<Guid>(),
            new Dictionary<Guid, double>(), new Dictionary<Guid, IReadOnlySet<string>>(), new(3, 2, 0, 80, 0), Today);
        Assert.Equal(expected, plan.RequiredYears);
        Assert.Equal(0, plan.EvidencedYears);
    }
}
