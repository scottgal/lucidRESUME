using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Tests;

public sealed class EvidenceLedgerBuilderTests
{
    [Fact]
    public void Rebuild_CreatesStableClaimsAndEvidenceFromIngestedData()
    {
        var resume = ResumeDocument.Create("resume.docx", "application/docx", 100);
        var experience = new WorkExperience
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Company = "Example Corp",
            Title = "Senior Engineer",
            Achievements = ["Built reliable ASP.NET Core APIs."],
            Technologies = ["ASP.NET Core"]
        };
        resume.Experience.Add(experience);
        resume.Skills.Add(new Skill { Name = "ASP.NET Core" });
        resume.Entities.Add(new ExtractedEntity
        {
            EntityId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Value = "ASP.NET Core",
            Classification = "NerSkill",
            Source = DetectionSource.Ner,
            Confidence = 0.91
        });

        var first = EvidenceLedgerBuilder.Rebuild(resume);
        var second = EvidenceLedgerBuilder.Rebuild(resume);

        Assert.Equal(first.SourceRevision, second.SourceRevision);
        Assert.Equal(first.Evidence.Select(item => item.Id), second.Evidence.Select(item => item.Id));
        var achievement = second.Claims.Single(claim => claim.Id.EndsWith(":achievement:1", StringComparison.Ordinal));
        Assert.Equal(["ASP.NET Core"], achievement.Concepts);
        Assert.Single(achievement.EvidenceIds);
        Assert.StartsWith("fnv1a64:", second.Evidence.Single(item => item.Id == achievement.EvidenceIds[0]).FastHash);
    }

    [Fact]
    public void EnsureCurrent_RebuildsAfterSourceDrift()
    {
        var resume = ResumeDocument.Create("resume.docx", "application/docx", 100);
        resume.Personal.Summary = "Original summary.";
        var original = EvidenceLedgerBuilder.Rebuild(resume);
        var originalHash = original.Evidence.Single(item => item.Locator == "personal:summary").FastHash;

        resume.Personal.Summary = "Changed summary.";
        var refreshed = EvidenceLedgerBuilder.EnsureCurrent(resume);

        Assert.NotEqual(original.SourceRevision, refreshed.SourceRevision);
        Assert.NotEqual(originalHash, refreshed.Evidence.Single(item => item.Locator == "personal:summary").FastHash);
    }

    [Fact]
    public void Rebuild_KeepsMachineExtractionDerivedUntilReview()
    {
        var resume = ResumeDocument.Create("resume.docx", "application/docx", 100);
        resume.Skills.Add(new Skill { Name = "Kubernetes", ImportSources = ["LLM extraction"] });
        resume.Entities.Add(new ExtractedEntity
        {
            Value = "Terraform",
            Classification = "NerSkill",
            Source = DetectionSource.Ner,
            Confidence = 0.9
        });

        var ledger = EvidenceLedgerBuilder.Rebuild(resume);
        var machineClaims = ledger.Claims.Where(claim =>
            claim.Statement is "Kubernetes" or "Terraform").ToList();

        Assert.Equal(2, machineClaims.Count);
        Assert.All(machineClaims, claim => Assert.Equal("derived", claim.Origin));
        Assert.All(machineClaims, claim => Assert.Equal("required", claim.Review));
    }

    [Fact]
    public void RebuildFromSources_PreservesOriginalDocumentEvidenceIds()
    {
        var first = ResumeDocument.Create("first.docx", "application/docx", 10);
        first.Skills.Add(new Skill { Name = "Kubernetes" });
        var second = ResumeDocument.Create("second.pdf", "application/pdf", 10);
        second.Skills.Add(new Skill { Name = "Kubernetes" });
        var firstLedger = EvidenceLedgerBuilder.Rebuild(first);
        var secondLedger = EvidenceLedgerBuilder.Rebuild(second);
        var merged = ResumeDocument.Create("merged", "application/lucidresume", 0);
        merged.Skills.Add(new Skill { Name = "Kubernetes" });

        var ledger = EvidenceLedgerBuilder.RebuildFromSources(merged, [firstLedger, secondLedger]);

        var claim = Assert.Single(ledger.Claims);
        Assert.Equal(2, claim.EvidenceIds.Count);
        Assert.Contains(claim.EvidenceIds, id => id.Contains(first.ResumeId.ToString("N"), StringComparison.Ordinal));
        Assert.Contains(claim.EvidenceIds, id => id.Contains(second.ResumeId.ToString("N"), StringComparison.Ordinal));
        Assert.DoesNotContain(claim.EvidenceIds, id => id.Contains(merged.ResumeId.ToString("N"), StringComparison.Ordinal));
    }
}
