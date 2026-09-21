using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class ResumeDecisionResolverTests
{
    [Fact]
    public async Task UsesRulesFirstAndProviderOnlyForUnresolvedHeading()
    {
        var provider = new FakeProvider("experience", 0.9, 0.08, 0.02);
        var sut = Create(provider);
        var resume = ResumeDocument.Create("test.docx", "application/docx", 10);
        DocumentSection[] sections =
        [
            new() { Heading = "Technical Skills", Body = "C#", Level = 1 },
            new() { Heading = "Where I made a difference", Body = "Acme, 2020-present", Level = 1 }
        ];

        var result = await sut.ResolveSectionsAsync(resume, sections);

        Assert.Null(result[0].SemanticType); // The normal deterministic parser still owns this.
        Assert.Equal("Experience", result[1].SemanticType);
        Assert.Equal(1, provider.CallCount);
        var decision = Assert.Single(resume.IngestionDecisions);
        Assert.True(decision.Accepted);
        Assert.StartsWith("fnv1a64:", decision.SourceHash);
        Assert.StartsWith("fnv1a64:", decision.CandidateSetHash);
        Assert.Equal(0.82, decision.Margin, 6);
    }

    [Fact]
    public async Task KeepsAmbiguousDecisionForReviewWithoutApplyingIt()
    {
        var provider = new FakeProvider("experience", 0.55, 0.40, 0.05);
        var sut = Create(provider);
        var resume = ResumeDocument.Create("test.docx", "application/docx", 10);
        DocumentSection[] sections =
        [
            new() { Heading = "Highlights", Body = "A mixture of work and awards", Level = 1 }
        ];

        var result = await sut.ResolveSectionsAsync(resume, sections);

        Assert.Null(result[0].SemanticType);
        Assert.False(Assert.Single(resume.IngestionDecisions).Accepted);
    }

    [Fact]
    public async Task NameDecisionCanOnlySelectAnExtractedCandidate()
    {
        var provider = new FakeProvider("candidate_1", 0.92, 0.05, 0.03);
        var sut = Create(provider);
        var resume = ResumeDocument.Create("test.docx", "application/docx", 10);
        resume.Personal.FullName = "Engineering Director";
        resume.Entities.Add(Core.Models.Extraction.ExtractedEntity.Create(
            "Alex Example", "PersonName", Core.Models.Extraction.DetectionSource.Ner, 0.72, 1));

        await sut.ResolveNameAsync(resume, "Alex Example\nEngineering Director\nexample@example.com");

        Assert.Equal("Alex Example", resume.Personal.FullName);
        var decision = Assert.Single(resume.IngestionDecisions);
        Assert.Equal(ResumeDecisionResolver.NameContractVersion, decision.ContractVersion);
        Assert.Equal("Alex Example", decision.SelectedValue);
        Assert.True(decision.Accepted);
    }

    [Fact]
    public async Task CompanyDecisionFillsMissingValueButNeverOverwritesOne()
    {
        var provider = new FakeProvider("candidate_1", 0.9, 0.06, 0.04);
        var sut = Create(provider);
        var resume = ResumeDocument.Create("test.docx", "application/docx", 10);
        resume.Entities.Add(Core.Models.Extraction.ExtractedEntity.Create(
            "Example Corp", "Organization", Core.Models.Extraction.DetectionSource.Ner, 0.8, 1));
        resume.Experience.Add(new WorkExperience { Title = "Lead Engineer", Achievements = ["Led delivery at Example Corp"] });
        resume.Experience.Add(new WorkExperience { Title = "CTO", Company = "Known Ltd" });

        await sut.ResolveMissingCompaniesAsync(resume);

        Assert.Equal("Example Corp", resume.Experience[0].Company);
        Assert.Equal("Known Ltd", resume.Experience[1].Company);
        Assert.Single(resume.IngestionDecisions);
    }

    private static ResumeDecisionResolver Create(IResumeDecisionProvider provider) => new(
        provider,
        Options.Create(new ResumeDecisionPolicyOptions
        {
            AcceptanceProbability = 0.80,
            MinimumMargin = 0.20
        }),
        NullLogger<ResumeDecisionResolver>.Instance);

    private sealed class FakeProvider(string selected, double selectedProbability, double runnerUp, double other)
        : IResumeDecisionProvider
    {
        public int CallCount { get; private set; }
        public string ProviderName => "fake";

        public Task<ResumeDecisionResult> DecideAsync(ResumeDecisionRequest request, CancellationToken ct = default)
        {
            CallCount++;
            var probabilities = request.Candidates.Keys.ToDictionary(key => key, _ => 0d);
            probabilities[selected] = selectedProbability;
            var runnerUpKey = request.Candidates.Keys.FirstOrDefault(key => key != selected && key is not "other" and not "none");
            if (runnerUpKey is not null) probabilities[runnerUpKey] = runnerUp;
            if (probabilities.ContainsKey("other")) probabilities["other"] = other;
            if (probabilities.ContainsKey("none")) probabilities["none"] = other;
            return Task.FromResult(new ResumeDecisionResult(
                selected, probabilities, 0.75, ProviderName, "fake-v1"));
        }
    }
}
