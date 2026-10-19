using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Matching.Tests;

public sealed class LedgerProjectionQualityTests
{
    [Fact]
    public async Task Audited_service_aliases_count_once_and_retain_the_advert_excerpt()
    {
        var job = new JobDescription
        {
            RawText = "Essential: Service Bus and Azure Service Bus integrations.",
            RequiredSkills = ["Azure Service Bus", "Service Bus"],
            SkillDecisions = [new("Azure Service Bus", "skill", "conservative", SourceText: "Service Bus and Azure Service Bus integrations."),
                new("Service Bus", "skill", "conservative")]
        };
        var ledger = await new JdSkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(job);
        Assert.Equal("Service Bus and Azure Service Bus integrations.", Assert.Single(ledger.Requirements).SourceText);
    }

    [Fact]
    public async Task Matching_consumes_saved_classifications_without_reextracting_rejected_noise()
    {
        var detector = new UnexpectedDetector();
        var job = new JobDescription
        {
            RawText = "C# required. Benefits: Pension.", SkillAnalysisVersion = "job-skills-v2:offline",
            RequiredSkills = ["C#", "Pension"],
            SkillDecisions = [new("C#", "skill", "conservative"), new("Pension", "noise", "conservative")]
        };
        var ledger = await new JdSkillLedgerBuilder(new EmptyEmbedder(), [detector]).BuildAsync(job);
        Assert.Equal("C#", Assert.Single(ledger.Requirements).SkillName);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public async Task Complete_competency_accounts_are_not_dropped_or_split_at_commas()
    {
        var job = new JobDescription { RawText = "Original advert", Competencies =
            [new("Production engineering, integration and reliability across a distributed system", "Own production systems and their reliability.", RequirementPriority.Required)] };
        var ledger = await new JdSkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(job);
        Assert.Equal(job.Competencies[0].Name, Assert.Single(ledger.Required).SkillName);
        Assert.Equal(job.Competencies[0].Description, ledger.Required[0].SourceText);
    }

    [Fact]
    public async Task Coverage_ignores_rejected_or_negated_accounts()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Used Kubernetes in production.", "No experience with AWS."] });
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        ledger.Claims.Single(claim => claim.Statement == "Used Kubernetes in production.").Review = "rejected";
        var report = await new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())), Options.Create(new CoverageOptions()))
            .AnalyseAsync(resume, new() { RequiredSkills = ["Kubernetes", "AWS"] });
        Assert.All(report.Entries, entry => Assert.False(entry.IsCovered));
    }

    [Fact]
    public async Task Complete_career_retains_literal_role_tools_and_their_source_binding()
    {
        var resume = ResumeDocument.Create("career", lucidRESUME.Core.Persistence.CompleteCareerRecord.ContentType, 0);
        resume.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Built Azure Functions using SignalR."] });
        var ledger = await new SkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(resume);
        var signal = Assert.Single(ledger.Entries, entry => entry.SkillName == "SignalR");
        Assert.Contains(signal.Evidence, evidence => evidence.ExperienceId == resume.Experience[0].Id && evidence.SourceReference is not null && evidence.SourceText.Contains("SignalR"));
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
    private sealed class UnexpectedDetector : IEntityDetector
    {
        public string DetectorId => "unexpected"; public int Priority => 0; public int Calls;
        public Task<IReadOnlyList<ExtractedEntity>> DetectAsync(DetectionContext context, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Matching must not run extraction"); }
    }
}
