using lucidRESUME.AI;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public class TailoringPromptBuilderTests
{
    private static CoverageReport MakeCoverage(CompanyType type,
        params (string text, RequirementPriority pri, string? evidence)[] entries)
    {
        var list = entries.Select(e => new CoverageEntry(
            new JdRequirement(e.text, e.pri),
            e.evidence,
            e.evidence is null ? null : "Skills[0]",
            e.evidence is null ? 0f : 1f)).ToList().AsReadOnly();
        return new CoverageReport(list, type, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Build_RequiredGaps_AppearInPrompt()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane Smith\n## Skills\n- C#";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Unknown,
            ("C#",         RequirementPriority.Required, "C#"),
            ("Kubernetes", RequirementPriority.Required, null));

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("Kubernetes", prompt);
        Assert.Contains("not covered", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StartupTone_InjectsStartupGuidance()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Startup);

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("ownership", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_EnterpriseTone_InjectsEnterpriseGuidance()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Enterprise);

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("process", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_CoveredRequirements_ListedFirst()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Unknown,
            ("C#",         RequirementPriority.Required, "C#"),
            ("Kubernetes", RequirementPriority.Required, null));

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        int coveredIdx = prompt.IndexOf("C#", StringComparison.Ordinal);
        int gapIdx     = prompt.IndexOf("Kubernetes", StringComparison.Ordinal);
        Assert.True(coveredIdx < gapIdx, "Covered requirements should appear before gaps");
    }
}
