using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.JobML;

namespace lucidRESUME.Matching.Tests;

public sealed class JobMlSkillLedgerTests
{
    [Fact]
    public async Task RawImport_DoesNotPromoteLlmOrNerSkillsBeforeReview()
    {
        var resume = ResumeDocument.Create("resume.docx", "application/docx", 100);
        resume.Skills.Add(new Skill { Name = "Kubernetes", ImportSources = ["LLM extraction"] });
        resume.Entities.Add(new Core.Models.Extraction.ExtractedEntity
        {
            Value = "Terraform",
            Classification = "NerSkill",
            Source = Core.Models.Extraction.DetectionSource.Ner,
            Confidence = 0.9
        });

        var ledger = await new SkillLedgerBuilder(new UnusedEmbeddingService()).BuildAsync(resume);

        Assert.Empty(ledger.Entries);
    }

    [Fact]
    public async Task PublishedJobMl_UsesOnlyAcceptedClaimsWithValidEvidence()
    {
        const string markdown = """
            # Jane Smith

            ## Experience

            ### Example Corp {#example-corp}

            Operated Kubernetes workloads in production.
            """;
        var file = JobMlDraftGenerator.Generate(markdown);
        file.Data.Concepts.Add(new JobMlConcept
        {
            Id = "kubernetes",
            Type = "skill",
            Name = "Kubernetes"
        });
        file.Data.Claims[0].Concepts.Skills.Add("kubernetes");

        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);
        resume.Skills.Add(new Skill { Name = "Unreviewed legacy skill" });
        var parser = new JobMlParser();
        resume.JobMlSource = parser.Serialize(file);

        var builder = new SkillLedgerBuilder(new UnusedEmbeddingService());
        var beforeReview = await builder.BuildAsync(resume);
        file.Data.Claims[0].Review = "accepted";
        resume.JobMlSource = parser.Serialize(file);
        var afterReview = await builder.BuildAsync(resume);

        Assert.Empty(beforeReview.Entries);
        var entry = Assert.Single(afterReview.Entries);
        Assert.Equal("Kubernetes", entry.SkillName);
        Assert.Equal(EvidenceSource.JobMlClaim, Assert.Single(entry.Evidence).Source);
        Assert.DoesNotContain(afterReview.Entries, item => item.SkillName == "Unreviewed legacy skill");
    }

    [Fact]
    public async Task PublishedJobMl_WithDriftedEvidenceIsRejected()
    {
        const string markdown = """
            # Jane Smith

            ## Experience

            ### Example Corp {#example-corp}

            Operated Kubernetes workloads in production.
            """;
        var file = JobMlDraftGenerator.Generate(markdown);
        file.Data.Claims[0].Review = "accepted";
        file = file with { Markdown = file.Markdown.Replace("Operated", "Observed") };
        var resume = ResumeDocument.Create("resume.md", "text/markdown", markdown.Length);
        resume.JobMlSource = new JobMlParser().Serialize(file);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new SkillLedgerBuilder(new UnusedEmbeddingService()).BuildAsync(resume));

        Assert.Contains("integrity errors", exception.Message);
    }

    private sealed class UnusedEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new InvalidOperationException("Reviewed JobML matching must not infer unreviewed skills.");

        public float CosineSimilarity(float[] a, float[] b) =>
            throw new InvalidOperationException("Reviewed JobML matching must not infer unreviewed skills.");
    }
}
