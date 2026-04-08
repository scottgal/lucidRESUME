using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.GitHub.Tests;

/// <summary>
/// Simple embedding service that uses string hash as a proxy.
/// Identical strings will have similarity 1.0, different strings ~0.
/// </summary>
sealed class TestEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var hash = text.ToLowerInvariant().GetHashCode();
        var emb = new float[8];
        for (var i = 0; i < 8; i++)
            emb[i] = ((hash >> (i * 4)) & 0xF) / 15f;
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

public class ResumeDocumentMergerTests
{
    private readonly ResumeDocumentMerger _merger = new(new TestEmbeddingService());

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
}
