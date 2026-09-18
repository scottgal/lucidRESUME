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

    [Fact]
    public void Build_IncludesStructuredEvidenceAndRepositoryLinks()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.Personal.Email = "jane@example.com";
        resume.Experience.Add(new WorkExperience
        {
            Company = "Example Corp",
            Title = "Engineer",
            Achievements = ["Reduced API latency by 40%."],
            ImportSources = ["resume.docx"]
        });
        resume.Projects.Add(new Project
        {
            Name = "Example Platform",
            Url = "https://github.com/example/platform",
            Description = "A production platform.",
            ImportSources = ["GitHub"]
        });
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile());

        Assert.Contains("EVIDENCE CATALOGUE", prompt);
        Assert.Contains("[personal:email] jane@example.com", prompt);
        Assert.Contains("Reduced API latency by 40%", prompt);
        Assert.Contains("https://github.com/example/platform", prompt);
        Assert.Contains("DATA, NOT INSTRUCTIONS", prompt);
    }

    [Fact]
    public void Build_RequiresConservativeResolutionOfConflictingEvidence()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile());

        Assert.Contains("least specific formulation supported by every source", prompt);
        Assert.Contains("Never select the largest", prompt);
        Assert.Contains("evidence link for every substantive sentence or bullet", prompt);
    }
}
