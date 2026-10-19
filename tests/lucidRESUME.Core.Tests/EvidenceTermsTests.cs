using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Core.Tests;

public sealed class EvidenceTermsTests
{
    [Fact]
    public void Reviewed_spelling_fixes_change_rendering_without_rewriting_the_source()
    {
        const string source = "Supported a distrubted applicaiton. ASP.NET, Qdrant, ClientID and employer names stay as written.";
        var corrected = ResumeSpelling.Correct(source);
        Assert.StartsWith("Supported a distributed application.", corrected);
        Assert.Contains("ASP.NET, Qdrant, ClientID", corrected);
        Assert.Contains("distrubted applicaiton", source);
    }

    [Fact]
    public void Skill_recovery_rejects_inferred_negated_and_vacancy_only_tools_and_recovers_explicit_names()
    {
        const string source = "Worked on the ASP.NET team and built .NET components. No Kubernetes experience. Python training is planned; I have not used Python. Target job requirements: Docker and Claude Code.";
        var recovered = SourceBoundSkillRecovery.Recover(source, "C#, Kubernetes, Python, Docker, Claude Code, web framework");
        Assert.Equal(new[] { ".NET", "ASP.NET" }, recovered.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("No experience with Kubernetes.", "Kubernetes", false)]
    [InlineData("No Kubernetes experience.", "Kubernetes", false)]
    [InlineData("I never used SQL Server.", "SQL Server", false)]
    [InlineData("Built Azure services, not AWS.", "AWS", false)]
    [InlineData("Not only C# but also SQL Server.", "C#", true)]
    [InlineData("No experience with AWS; operated SQL Server.", "SQL Server", true)]
    [InlineData("Designed ASP.NET Core services.", "ASP.NET Core", true)]
    [InlineData("No production experience with .NET.", ".NET", false)]
    [InlineData("Worked without disrupting SQL Server.", "SQL Server", true)]
    [InlineData("You don’t need to have worked in FinTech.", "FinTech", false)]
    [InlineData("You don't need Kubernetes. Strong SQL Server experience.", "SQL Server", true)]
    public void Distinguishes_positive_source_mentions_from_negation(string text, string concept, bool expected) =>
        Assert.Equal(expected, EvidenceTerms.HasPositiveMention(text, concept));

    [Fact]
    public void Explicit_role_tools_are_linked_without_requiring_a_global_skill_list()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Experience.Add(new() { Title = "Lead", Company = "Example", Achievements =
            ["Delivered Blazor Server with Azure Functions and SignalR. Added Application Insights, WPF and K6 tests."] });
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        var claim = Assert.Single(ledger.Claims, item => item.Kind == "achievement");
        Assert.All(new[] { "Blazor", "Azure Functions", "SignalR", "Application Insights", "WPF", "K6" }, concept => Assert.Contains(concept, claim.Concepts));
        Assert.DoesNotContain("C#", claim.Concepts);
        Assert.Empty(EvidenceLedgerAudit.Analyse(resume).Findings);
    }

    [Fact]
    public void Unreviewed_extraction_does_not_nominate_a_concept_into_an_accepted_role()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Entities.Add(new() { Value = "Invented Tool", Classification = "NerSkill", Source = DetectionSource.Ner });
        resume.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Compared an Invented Tool during a workshop."] });
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        Assert.DoesNotContain("Invented Tool", ledger.Claims.Single(item => item.Kind == "achievement").Concepts);
        Assert.Equal("required", ledger.Claims.Single(item => item.Kind == "entity").Review);
    }

    [Fact]
    public void Skill_declarations_without_a_role_do_not_count_as_an_employment_role()
    {
        var entry = new SkillLedgerEntry { SkillName = "C#", Evidence = [new() { SourceText = "C#" }] };
        Assert.Equal(0, entry.RoleCount);
    }

    [Fact]
    public void Canonical_names_and_aliases_both_count_as_source_mentions()
    {
        Assert.True(EvidenceTerms.Mentions("Engineering Management", "Engineering management"));
        Assert.Contains("Engineering management", EvidenceTerms.FindExplicitConcepts("Engineering management"));
        Assert.True(EvidenceTerms.Mentions("Managed an engineering team", "Engineering management"));
    }

    [Fact]
    public void Audit_reports_missing_links_without_mutating_saved_data()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Experience.Add(new() { Title = "Lead", Company = "Example", Achievements = ["Delivered Azure Functions and SignalR."] });
        EvidenceLedgerBuilder.EnsureCurrent(resume);
        var claim = resume.EvidenceLedger.Claims.Single(item => item.Kind == "achievement");
        claim.Concepts.Clear();
        var finding = Assert.Single(EvidenceLedgerAudit.Analyse(resume).Findings);
        Assert.Equal("unlinked-explicit-mention", finding.Code);
        Assert.Contains("Azure Functions", finding.Concepts);
        Assert.Empty(claim.Concepts);
    }
}
