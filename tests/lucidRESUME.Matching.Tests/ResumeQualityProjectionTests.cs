using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public sealed class ResumeQualityProjectionTests
{
    [Fact]
    public void Projection_accepts_two_focused_evidence_bullets()
    {
        var resume = ResumeDocument.Create("projection.md", "text/markdown", 0);
        resume.Personal.FullName = "Alex Example";
        resume.Personal.Email = "alex@example.com";
        resume.Personal.Summary = "Engineering leader.";
        resume.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = Guid.NewGuid(),
            SourceRevision = new string('a', 64)
        };
        resume.Experience.Add(new WorkExperience
        {
            Company = "Example Ltd",
            Title = "Engineering Lead",
            Achievements =
            [
                "Led 12 engineers through a platform migration completed in 6 months.",
                "Reduced deployment time by 40% across 8 production services."
            ]
        });

        var report = new ResumeQualityAnalyser().Analyse(resume);
        var bullets = report.Categories.Single(category => category.Name == "Bullet Quality");

        Assert.DoesNotContain(bullets.Findings, finding => finding.Code == "FEW_BULLETS");
    }

    [Fact]
    public void Quality_report_rejects_inverted_dates_and_import_placeholders()
    {
        var resume = ResumeDocument.Create("imported.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1);
        resume.Personal.FullName = "Alex Example";
        resume.Personal.Email = "alex@example.com";
        resume.Personal.Summary = "Desired Job Title: Senior Developer";
        resume.Experience.Add(new WorkExperience
        {
            Title = "Development Lead",
            Company = "Example Ltd",
            StartDate = new DateOnly(2024, 9, 1),
            EndDate = new DateOnly(2024, 5, 1),
            Achievements = ["Led 12 engineers through a platform migration completed in 6 months."]
        });
        resume.Experience.Add(new WorkExperience
        {
            Title = "Imported Role",
            Company = "Broken Template",
            StartDate = DateOnly.MinValue,
            Achievements = ["Imported from an old document template."]
        });

        var findings = new ResumeQualityAnalyser().Analyse(resume).Categories
            .SelectMany(category => category.Findings).ToList();

        Assert.Contains(findings, finding => finding.Code == "PLACEHOLDER_SUMMARY");
        Assert.Contains(findings, finding => finding.Code == "INVERTED_DATE_RANGE");
        Assert.Contains(findings, finding => finding.Code == "IMPLAUSIBLE_START_DATE");
    }

    [Fact]
    public void Quality_report_flags_probable_duplicate_company_role_variants()
    {
        var resume = ResumeDocument.Create("merged.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1);
        resume.Personal.FullName = "Alex Example";
        resume.Personal.Email = "alex@example.com";
        resume.Personal.Summary = "Engineering leader.";
        resume.Experience.Add(new WorkExperience
        {
            Title = "Development Lead",
            Company = "Example Limited",
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 7, 1),
            Achievements = ["Led 12 engineers through a platform migration completed in 6 months."]
        });
        resume.Experience.Add(new WorkExperience
        {
            Title = "Lead Developer",
            Company = "Example Ltd",
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 8, 1),
            Achievements = ["Reduced deployment time by 40% across 8 production services."]
        });

        var findings = new ResumeQualityAnalyser().Analyse(resume).Categories
            .SelectMany(category => category.Findings).ToList();

        Assert.Contains(findings, finding => finding.Code == "PROBABLE_DUPLICATE_ROLE");
    }
}
