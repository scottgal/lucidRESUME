using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching.Tests;

public class JobFilterExecutorTests
{
    private readonly JobFilterExecutor _executor = new();

    [Fact]
    public void Evaluate_SkillIn_MatchesWhenSkillPresent()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET", "Azure"];
        var filter = FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" });
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_SalaryMin_FiltersLowPay()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.Salary = new SalaryRange(40000, 50000);
        var filter = FilterNode.Leaf("salary_min", FilterOp.GreaterThanOrEqual, 70000m);
        Assert.False(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_All_RequiresAllChildren()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET"];
        job.IsRemote = true;
        var filter = FilterNode.All(
            FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" }),
            FilterNode.Leaf("work_model", FilterOp.Equal, "Remote")
        );
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_SkillIn_ReturnsFalse_WhenSkillAbsent()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["Python"];
        var filter = FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" });
        Assert.False(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_SkillNotIn_ReturnsTrueWhenSkillAbsent()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["Python"];
        var filter = FilterNode.Leaf("skills", FilterOp.NotIn, new[] { ".NET" });
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_WorkModel_HybridMatches()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.IsHybrid = true;
        var filter = FilterNode.Leaf("work_model", FilterOp.Equal, "Hybrid");
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_WorkModel_DefaultOnsite()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        var filter = FilterNode.Leaf("work_model", FilterOp.Equal, "Onsite");
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_SalaryMin_PassesWhenAboveThreshold()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.Salary = new SalaryRange(80000, 100000);
        var filter = FilterNode.Leaf("salary_min", FilterOp.GreaterThanOrEqual, 70000m);
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_SalaryBetween_Matches()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.Salary = new SalaryRange(75000, 90000);
        var filter = FilterNode.Leaf("salary_min", FilterOp.Between, 50000m, 100000m);
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_Any_ReturnsTrueIfOneChildMatches()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["Python"];
        job.IsRemote = true;
        var filter = FilterNode.Any(
            FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" }),  // false
            FilterNode.Leaf("work_model", FilterOp.Equal, "Remote")    // true
        );
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_Not_InvertsChild()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.IsRemote = true;
        var filter = FilterNode.Not(FilterNode.Leaf("work_model", FilterOp.Equal, "Onsite"));
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_NullSalary_ReturnsFalseForNumericOp()
    {
        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        // No salary set
        var filter = FilterNode.Leaf("salary_min", FilterOp.GreaterThanOrEqual, 50000m);
        Assert.False(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_Title_ContainsMatch()
    {
        var job = JobDescription.Create("Senior .NET Developer", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Senior .NET Developer";
        var filter = FilterNode.Leaf("title", FilterOp.Contains, ".NET");
        Assert.True(_executor.Evaluate(filter, job));
    }

    [Fact]
    public void Evaluate_Company_Equal_CaseInsensitive()
    {
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        job.Company = "Acme Corp";
        var filter = FilterNode.Leaf("company", FilterOp.Equal, "acme corp");
        Assert.True(_executor.Evaluate(filter, job));
    }
}
