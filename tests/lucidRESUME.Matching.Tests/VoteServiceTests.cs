using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Matching;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Matching.Tests;

public class VoteServiceTests
{
    private static AspectExtractor CreateExtractor() =>
        new(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())));

    private readonly VoteService _service = new(CreateExtractor());

    // ── BuildAutoFilters ────────────────────────────────────────────────────

    [Fact]
    public void BuildAutoFilters_ReturnsNull_WhenNoDownvotes()
    {
        var profile = new UserProfile();
        Assert.Null(_service.BuildAutoFilters(profile));
    }

    [Fact]
    public void BuildAutoFilters_ReturnsNull_WhenDownvotesBelowThreshold()
    {
        var profile = new UserProfile();
        profile.VoteDown(AspectType.WorkModel, "Onsite"); // score = -1
        profile.VoteDown(AspectType.WorkModel, "Onsite"); // score = -2
        Assert.Null(_service.BuildAutoFilters(profile));
    }

    [Fact]
    public void BuildAutoFilters_ReturnsFilter_WhenScoreReachesThreshold()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 3; i++)
            profile.VoteDown(AspectType.WorkModel, "Onsite");

        var filter = _service.BuildAutoFilters(profile);

        Assert.NotNull(filter);
        Assert.Equal(FilterLogic.All, filter!.Logic);
        Assert.Single(filter.Children);

        var leaf = filter.Children[0];
        Assert.Equal(FilterOp.NotEqual, leaf.Op);
        Assert.Equal("work_model", leaf.Field);
        Assert.Equal("Onsite", leaf.Value);
    }

    [Fact]
    public void BuildAutoFilters_UsesNotIn_ForMultipleValuesOfSameType()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 3; i++)
        {
            profile.VoteDown(AspectType.Industry, "Finance");
            profile.VoteDown(AspectType.Industry, "Insurance");
        }

        var filter = _service.BuildAutoFilters(profile);

        Assert.NotNull(filter);
        var leaf = filter!.Children.Single(c => c.Field == "industry");
        Assert.Equal(FilterOp.NotIn, leaf.Op);
        var values = (string[])leaf.Value!;
        Assert.Contains("Finance", values);
        Assert.Contains("Insurance", values);
    }

    [Fact]
    public void BuildAutoFilters_SkipsSalaryAndCultureSignal_AspectTypes()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 3; i++)
        {
            profile.VoteDown(AspectType.SalaryBand, "Under £40k");
            profile.VoteDown(AspectType.CultureSignal, "Fast-paced");
        }

        // Neither SalaryBand nor CultureSignal maps to a field - should produce null
        Assert.Null(_service.BuildAutoFilters(profile));
    }

    [Fact]
    public void BuildAutoFilters_IncludesMultipleTypes_WhenBothQualify()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 3; i++)
        {
            profile.VoteDown(AspectType.WorkModel, "Onsite");
            profile.VoteDown(AspectType.CompanyType, "Agency");
        }

        var filter = _service.BuildAutoFilters(profile);

        Assert.NotNull(filter);
        Assert.Equal(2, filter!.Children.Count);
        Assert.Contains(filter.Children, c => c.Field == "work_model");
        Assert.Contains(filter.Children, c => c.Field == "company_type");
    }

    // ── GetScoredAspects ────────────────────────────────────────────────────

    [Fact]
    public void GetScoredAspects_ReflectsVoteScores()
    {
        var job = JobDescription.Create("Remote .NET role", new JobSource { Type = JobSourceType.PastedText });
        job.IsRemote = true;

        var profile = new UserProfile();
        profile.VoteUp(AspectType.WorkModel, "Remote");
        profile.VoteUp(AspectType.WorkModel, "Remote");

        var aspects = _service.GetScoredAspects(job, profile);

        var remoteAspect = aspects.Single(a => a.Aspect.Type == AspectType.WorkModel);
        Assert.Equal(2, remoteAspect.CurrentScore);
    }
}