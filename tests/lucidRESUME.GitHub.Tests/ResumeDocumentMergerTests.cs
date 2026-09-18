using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using System.Security.Cryptography;
using System.Text;

namespace lucidRESUME.GitHub.Tests;

/// <summary>
/// Deterministic embedding test double. Do not use string.GetHashCode here: it is
/// process-randomized and made semantic-match tests intermittently merge unrelated skills.
/// </summary>
sealed class TestEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
        var emb = new float[hash.Length];
        for (var i = 0; i < hash.Length; i++)
            emb[i] = (hash[i] - 127.5f) / 127.5f;
        var norm = MathF.Sqrt(emb.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < 8; i++) emb[i] /= norm;
        return Task.FromResult(emb);
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        var dot = a.Zip(b, (x, y) => x * y).Sum();
        var normA = MathF.Sqrt(a.Sum(x => x * x));
        var normB = MathF.Sqrt(b.Sum(x => x * x));
        return normA > 0 && normB > 0 ? dot / (normA * normB) : 0;
    }
}

sealed class FailingEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        throw new InvalidDataException("simulated corrupt model");

    public float CosineSimilarity(float[] a, float[] b) => throw new NotSupportedException();
}

public class ResumeDocumentMergerTests
{
    private readonly ResumeDocumentMerger _merger = new(new TestEmbeddingService());

    [Fact]
    public async Task PreviewMerge_WhenEmbeddingModelFails_UsesDeterministicFallback()
    {
        var target = ResumeDocument.Create("base.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Ltd",
            Title = "Engineer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2022, 1, 1)
        });
        target.Skills.Add(new Skill { Name = "C#" });

        var incoming = ResumeDocument.Create("variant.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Ltd",
            Title = "Senior Engineer",
            StartDate = new DateOnly(2020, 2, 1),
            EndDate = new DateOnly(2022, 1, 1)
        });
        incoming.Experience.Add(new WorkExperience { Company = "Different Company", Title = "Consultant" });
        incoming.Skills.Add(new Skill { Name = "C#" });
        incoming.Skills.Add(new Skill { Name = "Kubernetes" });

        var preview = await new ResumeDocumentMerger(new FailingEmbeddingService())
            .PreviewMergeAsync(target, incoming, "variant.docx");

        Assert.Single(preview.MergedExperience);
        Assert.Single(preview.NewExperience);
        Assert.Single(preview.UpdatedSkills);
        Assert.Single(preview.NewSkills);
    }

    [Fact]
    public async Task MergeInto_MergesExperienceByCompanyAndDateOverlap()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Developer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 6, 1),
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Senior Developer",
            StartDate = new DateOnly(2020, 3, 1),
            EndDate = new DateOnly(2023, 5, 1),
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Single(target.Experience);
        Assert.Equal("Senior Developer", target.Experience[0].Title);
        Assert.Contains("LinkedIn", target.Experience[0].ImportSources);
    }

    [Fact]
    public async Task MergeInto_AddsNewExperience()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Totally Different Inc",
            StartDate = new DateOnly(2023, 6, 1),
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Experience.Count);
    }

    [Fact]
    public async Task MergeInto_DetectsNameMismatch()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "Jonathan Smith";

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Contains(anomalies, a => a.Type == AnomalyType.NameMismatch);
        Assert.Equal("John Smith", target.Personal.FullName);
    }

    [Fact]
    public async Task MergeInto_MergesSkillsByName()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Skills.Add(new Skill { Name = "C#", Category = "Language" });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Skills.Add(new Skill { Name = "C#", EndorsementCount = 5 });
        incoming.Skills.Add(new Skill { Name = "Docker" });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Skills.Count);
        var csharp = target.Skills.First(s => s.Name == "C#");
        Assert.Equal("Language", csharp.Category);
        Assert.Contains("LinkedIn", csharp.ImportSources);
    }

    [Fact]
    public async Task MergeInto_FillsPersonalInfoGaps()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";
        target.Personal.Email = "john@test.com";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "John Smith";
        incoming.Personal.Phone = "+44 123 456";
        incoming.Personal.Location = "London";

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Empty(anomalies);
        Assert.Equal("+44 123 456", target.Personal.Phone);
        Assert.Equal("London", target.Personal.Location);
    }

    [Fact]
    public async Task MergeInto_MergesAchievements()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
            Achievements = ["Built a platform", "Led a team"],
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
            Achievements = ["Built a platform", "Reduced costs by 30%"],
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Single(target.Experience);
        Assert.Equal(3, target.Experience[0].Achievements.Count);
    }

    [Fact]
    public async Task MergeInto_MergesReorderedDevelopmentLeadTitlesWithoutWideningConflictingDate()
    {
        var target = ResumeDocument.Create("new.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Dell",
            Title = "Development Lead",
            StartDate = new DateOnly(2011, 1, 1),
            EndDate = new DateOnly(2011, 7, 1)
        });
        var incoming = ResumeDocument.Create("old.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Dell Limited",
            Title = "Lead Developer",
            StartDate = new DateOnly(2010, 1, 1),
            EndDate = new DateOnly(2010, 8, 1)
        });

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "old.docx");

        Assert.Single(target.Experience);
        Assert.Equal(new DateOnly(2011, 1, 1), target.Experience[0].StartDate);
        Assert.Equal(new DateOnly(2011, 7, 1), target.Experience[0].EndDate);
        Assert.Contains(anomalies, anomaly => anomaly.Type == AnomalyType.DateMismatch);
    }

    [Fact]
    public async Task MergeInto_PreservesConsecutiveDistinctRolesAtSameCompany()
    {
        var target = ResumeDocument.Create("base.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Microsoft",
            Title = "Application Development Consultant",
            StartDate = new DateOnly(2005, 6, 1),
            EndDate = new DateOnly(2007, 1, 1)
        });
        var incoming = ResumeDocument.Create("base.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Microsoft",
            Title = "Program Manager",
            StartDate = new DateOnly(2007, 1, 1),
            EndDate = new DateOnly(2009, 10, 1)
        });

        await _merger.MergeIntoAsync(target, incoming, "base.docx");

        Assert.Equal(2, target.Experience.Count);
    }
}
