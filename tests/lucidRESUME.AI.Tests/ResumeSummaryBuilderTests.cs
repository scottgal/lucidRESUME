namespace lucidRESUME.AI.Tests;

public sealed class ResumeSummaryBuilderTests
{
    [Fact]
    public void Off_target_profile_retains_identity_and_uses_only_evidenced_job_focus()
    {
        var summary = ResumeSummaryBuilder.Build("Software engineer building internal AI agent runtimes.", ["PostgreSQL"], false);
        Assert.Equal("Software engineer with experience in PostgreSQL.", summary);
        Assert.DoesNotContain("AI", summary);
        Assert.True(ResumeSummaryBuilder.UsesIdentity(summary, "Software engineer building internal AI agent runtimes."));
    }

    [Fact]
    public void Relevant_authored_profile_is_preserved()
    {
        const string source = "Software engineer building reliable C# services.";
        Assert.Equal(source, ResumeSummaryBuilder.Build(source, ["C#"], true));
    }

    [Fact]
    public void Missing_identity_does_not_invent_a_title_or_seniority()
    {
        Assert.Equal("Experience with PostgreSQL and C#.", ResumeSummaryBuilder.Build("Worked on billing recovery.", ["PostgreSQL", "C#"], false));
    }

    [Theory]
    [InlineData("Designed PostgreSQL reporting queries and database backup recovery drills.", "PostgreSQL", "designing PostgreSQL reporting queries")]
    [InlineData("Built ASP.NET Core services for internal operations.", "ASP.NET Core", "building ASP.NET Core services")]
    [InlineData("Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms.", "PostgreSQL", "optimising PostgreSQL queries")]
    public void Account_focus_reuses_a_concrete_source_object_without_promoting_an_outcome(string source, string skill, string focus)
    {
        var account = ResumeSummaryBuilder.FindFocusAccount([source], [skill]);
        Assert.NotNull(account);
        Assert.Equal(focus, account.Phrase);
        Assert.Equal($"Software engineer with experience {focus}.", ResumeSummaryBuilder.Build("Software engineer building internal AI agent runtimes.", [skill], false, account));
    }

    [Theory]
    [InlineData("Built a Python prototype; it was never deployed to production.")]
    [InlineData("Helped build Python document workflows.")]
    [InlineData("Designed Python recovery workflows; the production effect remains unknown.")]
    public void Qualified_accounts_do_not_lose_their_status_in_an_extracted_focus(string source)
    {
        Assert.Null(ResumeSummaryBuilder.FindFocusAccount([source], ["Python"]));
    }

    [Fact]
    public void Summary_combines_short_themes_without_copying_an_entire_account_after_an_identity_prefix()
    {
        string[] source = ["Designed PostgreSQL reporting queries and database backup recovery drills.",
            "Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms."];
        var account = ResumeSummaryBuilder.FindFocusAccount(source, ["PostgreSQL"]);
        Assert.NotNull(account);
        var summary = ResumeSummaryBuilder.Build("Software engineer building internal AI agent runtimes.", ["PostgreSQL"], false, account);
        Assert.Equal("Software engineer with experience designing PostgreSQL reporting queries and optimising queries.", summary);
        Assert.False(lucidRESUME.Compiler.CompositionValidator.RepeatsSentence(summary, source));
        Assert.Equal(2, account.Sources.Count);
    }

    [Fact]
    public void Coordinated_technologies_keep_the_noun_that_describes_the_work()
    {
        var account = ResumeSummaryBuilder.FindFocusAccount(["Built C# and ASP.NET Core APIs with PostgreSQL for customer workflows."], ["PostgreSQL"]);
        Assert.NotNull(account);
        Assert.Equal("building C# and ASP.NET Core APIs", account.Phrase);
        Assert.Equal(["PostgreSQL"], account.RemainingSkills);
    }
}
