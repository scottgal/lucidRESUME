namespace lucidRESUME.AI.Tests;

public sealed class ResumeSummaryBuilderTests
{
    [Fact]
    public void Stated_professional_identity_can_follow_an_authored_product_account()
    {
        Assert.Equal("Technology leader", ResumeSummaryBuilder.Identity(
            "Over the past year I have built agentic products. Technology leader with two decades of experience."));
        Assert.Equal("Over the past year I have built agentic products.", ResumeSummaryBuilder.WithoutRepeatedIdentity(
            "Over the past year I have built agentic products. Technology leader with two decades of experience.",
            "Technology leader with experience in .NET."));
        const string qualified = "Built tools. Technology leader with no management experience.";
        Assert.Equal(qualified, ResumeSummaryBuilder.WithoutRepeatedIdentity(qualified, "Technology leader with experience in .NET."));
    }

    [Fact]
    public void Shared_summary_object_is_written_once_without_merging_different_objects()
    {
        var account = ResumeSummaryBuilder.FindFocusAccount(
            ["Designed PostgreSQL reporting queries and database backup drills.", "Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms."], ["PostgreSQL"]);
        Assert.Equal("designing and optimising PostgreSQL queries", account!.Phrase);
        var distinct = ResumeSummaryBuilder.FindFocusAccount(
            ["Built internal support agents using C#.", "Designed customer support agents using C#."], ["C#"]);
        Assert.Contains("internal", distinct!.Phrase);
        Assert.Contains("customer", distinct.Phrase);
    }

    [Fact]
    public void Relevant_first_person_product_account_is_preserved_with_its_qualifiers()
    {
        const string source = "Over the past year I have developed products with agentic LLMs, documenting the work in dozens of technical articles. Technology leader with two decades of experience.";
        Assert.Equal(source, ResumeSummaryBuilder.RelevantAuthoredAccount(source, "Applied AI Engineer building agentic systems with LLMs."));
        Assert.Null(ResumeSummaryBuilder.RelevantAuthoredAccount(source, "PostgreSQL database administration and backup recovery."));
    }

    [Fact]
    public void Authored_account_keeps_following_delivery_qualification()
    {
        const string source = "I have developed agentic software products with LLMs. They were never deployed to production.";
        Assert.Equal(source, ResumeSummaryBuilder.RelevantAuthoredAccount(source, "Applied AI Engineer building agentic products with LLMs."));
    }

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
        Assert.Equal("Software engineer with experience designing and optimising PostgreSQL queries.", summary);
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
