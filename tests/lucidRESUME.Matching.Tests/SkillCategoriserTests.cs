using lucidRESUME.Matching;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Matching.Tests;

public sealed class SkillCategoriserTests
{
    [Fact]
    public void UsesTheTaxonomyThatContainsTheSkill()
    {
        Assert.Equal("Sales", SkillCategoriser.CategoriseSkill("Salesforce"));
    }

    [Fact]
    public void ShortLanguageNamesDoNotMatchInsideOtherSkills()
    {
        Assert.Equal("Sales", SkillCategoriser.CategoriseSkill("Salesforce"));
        Assert.NotEqual("Language", SkillCategoriser.CategoriseSkill("Server administration"));
    }

    [Fact]
    public void SharedTaxonomyTermsRemainAmbiguousUntilContextIsAvailable()
    {
        Assert.Contains("finance", SkillTaxonomy.FindDomains("bookkeeping"));
        Assert.Contains("accounting", SkillTaxonomy.FindDomains("bookkeeping"));
        Assert.Null(SkillCategoriser.CategoriseSkill("bookkeeping"));
    }

    [Fact]
    public async Task NimbleCategoryMustPointToAnOriginalProsePassage()
    {
        var markdown = "## Expertise {#expertise}\n\nI configured AcmeFlow pipelines for several teams.\n";
        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);
        resume.RawMarkdown = markdown;
        resume.Skills.Add(new Skill { Name = "AcmeFlow" });
        resume.Skills.Add(new Skill { Name = "InventedSkill" });
        var provider = new StubDecisionProvider();

        await SkillCategoriser.CategoriseAmbiguousAsync(resume, provider);

        Assert.Equal("Tool", resume.Skills[0].Category);
        Assert.Null(resume.Skills[1].Category);
        var decision = Assert.Single(resume.IngestionDecisions);
        Assert.Equal("#expertise:p1", decision.SourceRef);
        Assert.Equal(MarkdownEvidenceIndex.Fingerprint(
            "I configured AcmeFlow pipelines for several teams."), decision.SourceHash);
        Assert.Equal("ollama-nimble", decision.Provider);
        Assert.True(decision.Accepted);
        Assert.Equal(1, provider.CallCount);
        SkillCategoriser.Categorise(resume);
        Assert.Contains("#expertise:p1", resume.Skills[0].SourceReferences);
    }

    private sealed class StubDecisionProvider : IResumeDecisionProvider
    {
        public string ProviderName => "ollama-nimble";
        public int CallCount { get; private set; }

        public Task<ResumeDecisionResult> DecideAsync(ResumeDecisionRequest request,
            CancellationToken ct = default)
        {
            CallCount++;
            Assert.Contains("AcmeFlow", request.State);
            Assert.Equal("#expertise:p1", request.SourceRef);
            return Task.FromResult(new ResumeDecisionResult("tool",
                request.Candidates.Keys.ToDictionary(key => key,
                    key => key == "tool" ? 0.94 : key == "other" ? 0.06 : 0d),
                0.94, ProviderName, "nimble"));
        }
    }
}
