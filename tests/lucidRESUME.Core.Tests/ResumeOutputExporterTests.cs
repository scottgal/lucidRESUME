using DocumentFormat.OpenXml.Packaging;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.JobML;

namespace lucidRESUME.Core.Tests;

public sealed class ResumeOutputExporterTests
{
    [Fact]
    public async Task DocxExport_ContainsHumanResumeMachineAreaAndArticleLink()
    {
        var resume = CreateResume(ResumeTemplateCatalog.ModernProfessionalId);

        var bytes = await new DocxExporter().ExportAsync(resume);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var mainPart = document.MainDocumentPart!;
        var text = mainPart.Document!.Body!.InnerText;
        Assert.Contains("Jane Smith", text);
        Assert.Contains("MACHINE AREA", text);
        Assert.Contains("jobml:", text);
        Assert.Contains(mainPart.HyperlinkRelationships,
            relationship => relationship.Uri.ToString() == JobMlArtifactComposer.ArticleUrl);
    }

    [Theory]
    [InlineData(ResumeTemplateCatalog.AtsClassicId)]
    [InlineData(ResumeTemplateCatalog.ModernProfessionalId)]
    [InlineData(ResumeTemplateCatalog.CompactTechnicalId)]
    public async Task PdfExport_RendersEveryTemplateWithMachineArea(string templateId)
    {
        var bytes = await new PdfExporter().ExportAsync(CreateResume(templateId));

        Assert.True(bytes.Length > 1_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task MarkdownExport_ReturnsPortableHumanAndJobMlArtifact()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);

        var bytes = await new MarkdownExporter().ExportAsync(resume);
        var markdown = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("# Jane Smith", markdown);
        Assert.Contains("## MACHINE AREA", markdown);
        Assert.Contains("```jobml", markdown);
    }

    [Fact]
    public void ArtifactBuilder_CreatesReversibleEvidenceBackedOutput()
    {
        var source = ResumeDocument.Create("source.md", "text/markdown", 1);
        source.Personal.FullName = "Jane Smith";
        source.Personal.Email = "jane@example.com";
        source.Skills.Add(new Skill { Name = "Kubernetes" });
        source.Experience.Add(new WorkExperience
        {
            Company = "Example Corp",
            Title = "Platform Engineer",
            StartDate = new DateOnly(2022, 1, 1),
            IsCurrent = true
        });
        source.Projects.Add(new Project
        {
            Name = "Atlas",
            Description = "Production orchestration platform",
            Technologies = ["Kubernetes"],
            Url = "https://github.com/example/atlas"
        });

        var generated = ResumeDocument.Create("generated.md", "text/markdown", 1);
        generated.RawMarkdown = "# Jane Smith\n\n## Experience\n\n### Platform Engineer | Example Corp | Jan 2022–Present\n\nOperated Kubernetes workloads in production.\n\n## Projects\n\n### Atlas\n\nOperated Kubernetes workloads in production.";
        generated.GenerationEvidenceLinks.Add(new GenerationEvidenceLink
        {
            OutputClaim = "Operated Kubernetes workloads in production.",
            EvidenceRefs = ["project:abc:description"]
        });
        var job = JobDescription.Create("Kubernetes platform role", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Platform Engineer";
        job.RequiredSkills.Add("Kubernetes");

        var artifact = new ResumeArtifactBuilder().Build(
            source, generated, job, ResumeTemplateCatalog.ModernProfessionalId);

        Assert.Equal("Jane Smith", artifact.Personal.FullName);
        Assert.Contains("## MACHINE AREA", artifact.JobMlSource);
        Assert.Contains("https://github.com/example/atlas", artifact.JobMlSource);
        Assert.Contains("fingerprint:", artifact.JobMlSource);
        Assert.Contains("type: source_ledger", artifact.JobMlSource);
        Assert.Contains("ledger://project:abc:description", artifact.JobMlSource);
        var experience = Assert.Single(artifact.Experience);
        Assert.Equal("Example Corp", experience.Company);
        Assert.Equal("Platform Engineer", experience.Title);
        Assert.Null(experience.Location);
        Assert.Equal("jane@example.com", artifact.Personal.Email);
        Assert.Contains("importance: required", artifact.JobMlSource);
        Assert.DoesNotContain("\n    all:", artifact.JobMlSource);
        Assert.DoesNotContain("MACHINE AREA", artifact.CanonicalMarkdown);
        Assert.Equal(job.JobId, artifact.TailoredForJobId);
    }

    private static ResumeDocument CreateResume(string templateId)
    {
        const string markdown = "# Jane Smith\n\n## Experience\n\n### Engineer at Example Corp\n\nBuilt a reliable platform for customers.";
        var file = JobMlDraftGenerator.Generate(markdown);
        var resume = ResumeDocument.Create("jane.md", "text/markdown", markdown.Length);
        resume.Personal.FullName = "Jane Smith";
        resume.Personal.Email = "jane@example.com";
        resume.Personal.Summary = "Evidence-focused software engineer.";
        resume.Experience.Add(new WorkExperience
        {
            Title = "Engineer",
            Company = "Example Corp",
            Achievements = ["Built a reliable platform for customers."]
        });
        resume.CanonicalMarkdown = file.Markdown;
        resume.JobMlSource = JobMlArtifactComposer.Compose(file);
        resume.OutputTemplateId = templateId;
        return resume;
    }
}
