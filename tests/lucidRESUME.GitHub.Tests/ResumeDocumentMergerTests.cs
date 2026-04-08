using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.GitHub.Tests;

public class ResumeDocumentMergerTests
{
    [Fact]
    public void MergeInto_MergesExperienceByCompanyAndDateOverlap()
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
            Company = "Acme Corp Ltd",  // different suffix
            Title = "Senior Developer", // different title
            StartDate = new DateOnly(2020, 3, 1), // slight date difference
            EndDate = new DateOnly(2023, 5, 1),
        });

        var anomalies = ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        // Should merge into 1 entry, not 2
        Assert.Single(target.Experience);
        // Keeps the longer title
        Assert.Equal("Senior Developer", target.Experience[0].Title);
        // Extends date range
        Assert.Equal(new DateOnly(2020, 1, 1), target.Experience[0].StartDate);
        Assert.Equal(new DateOnly(2023, 6, 1), target.Experience[0].EndDate);
        // Source tracked
        Assert.Contains("LinkedIn", target.Experience[0].ImportSources);
    }

    [Fact]
    public void MergeInto_AddsNewExperience()
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

        ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Experience.Count);
    }

    [Fact]
    public void MergeInto_DetectsNameMismatch()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "Jonathan Smith";

        var anomalies = ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Contains(anomalies, a => a.Type == AnomalyType.NameMismatch);
        // Original name preserved
        Assert.Equal("John Smith", target.Personal.FullName);
    }

    [Fact]
    public void MergeInto_DetectsDateMismatch()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Developer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Developer",
            StartDate = new DateOnly(2019, 6, 1), // 7 months off
            EndDate = new DateOnly(2023, 1, 1),
        });

        var anomalies = ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Contains(anomalies, a => a.Type == AnomalyType.DateMismatch);
    }

    [Fact]
    public void MergeInto_MergesSkillsByName()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Skills.Add(new Skill { Name = "C#", Category = "Language" });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Skills.Add(new Skill { Name = "C#", EndorsementCount = 5 });
        incoming.Skills.Add(new Skill { Name = "Docker" });

        ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Skills.Count);
        var csharp = target.Skills.First(s => s.Name == "C#");
        Assert.Equal("Language", csharp.Category); // preserved from target
        Assert.Contains("LinkedIn", csharp.ImportSources);
    }

    [Fact]
    public void MergeInto_FillsPersonalInfoGaps()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";
        target.Personal.Email = "john@test.com";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "John Smith";
        incoming.Personal.Phone = "+44 123 456";
        incoming.Personal.Location = "London";

        var anomalies = ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Empty(anomalies); // same name, no conflict
        Assert.Equal("john@test.com", target.Personal.Email); // preserved
        Assert.Equal("+44 123 456", target.Personal.Phone); // filled from LinkedIn
        Assert.Equal("London", target.Personal.Location); // filled from LinkedIn
    }

    [Fact]
    public void MergeInto_MergesAchievements()
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
            Achievements = ["Built a platform", "Reduced costs by 30%"], // 1 overlap, 1 new
        });

        ResumeDocumentMerger.MergeInto(target, incoming, "LinkedIn");

        Assert.Single(target.Experience);
        Assert.Equal(3, target.Experience[0].Achievements.Count); // 2 original + 1 new
    }
}
