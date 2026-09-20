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
}
