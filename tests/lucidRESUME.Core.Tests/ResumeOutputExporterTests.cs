using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;
using UglyToad.PdfPig;

namespace lucidRESUME.Core.Tests;

public sealed class ResumeOutputExporterTests
{
    [Fact]
    public async Task DocxExport_ContainsHumanResumeAndCompactScientificReferences()
    {
        var resume = CreateResume(ResumeTemplateCatalog.ModernProfessionalId);

        var bytes = await new DocxExporter().ExportAsync(resume);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var mainPart = document.MainDocumentPart!;
        var text = mainPart.Document!.Body!.InnerText;
        Assert.Contains("Jane Smith", text);
        Assert.Contains("Platform Engineer", text);
        Assert.Contains("Target role: Platform Engineer", text);
        Assert.Contains("References", text);
        Assert.Contains("cJobML 0.1", text);
        Assert.Contains("[1]", text);
        Assert.Contains("Reduced RAG", text);
        Assert.DoesNotContain("MACHINE AREA", text);
        Assert.DoesNotContain("fingerprint:", text);
        Assert.Contains(mainPart.HyperlinkRelationships,
            relationship => relationship.Uri.ToString() == "https://mostlylucid.net/reduced-rag");
        Assert.Contains(mainPart.HyperlinkRelationships,
            relationship => relationship.Uri.ToString() == "https://example.com/jane.jobml");
        Assert.Single(mainPart.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>(),
            hyperlink => hyperlink.Anchor?.Value == "ref-1");
        Assert.NotNull(mainPart.NumberingDefinitionsPart?.Numbering);
        var paragraphs = mainPart.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
        Assert.Contains(paragraphs, paragraph =>
            paragraph.InnerText.Contains("Engineer | Example Corp", StringComparison.Ordinal) &&
            paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading3");
        var achievement = Assert.Single(paragraphs,
            paragraph => paragraph.InnerText.StartsWith("Built a reliable platform", StringComparison.Ordinal));
        Assert.NotNull(achievement.ParagraphProperties?.NumberingProperties);
        Assert.DoesNotContain("•", achievement.InnerText);
        var validationErrors = new OpenXmlValidator().Validate(document).ToList();
        Assert.True(validationErrors.Count == 0,
            string.Join(Environment.NewLine, validationErrors.Select(error =>
                $"{error.Part?.Uri}: {error.Path?.XPath}: {error.Description}")));
    }

    [Theory]
    [InlineData(ResumeTemplateCatalog.AtsClassicId)]
    [InlineData(ResumeTemplateCatalog.ModernProfessionalId)]
    [InlineData(ResumeTemplateCatalog.CompactTechnicalId)]
    public async Task DocxExport_IsValidOpenXmlForEveryTemplate(string templateId)
    {
        var bytes = await new DocxExporter().ExportAsync(CreateResume(templateId));

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var validationErrors = new OpenXmlValidator().Validate(document).ToList();

        Assert.True(validationErrors.Count == 0,
            string.Join(Environment.NewLine, validationErrors.Select(error =>
                $"{error.Part?.Uri}: {error.Path?.XPath}: {error.Description}")));
    }

    [Theory]
    [InlineData(ResumeTemplateCatalog.AtsClassicId)]
    [InlineData(ResumeTemplateCatalog.ModernProfessionalId)]
    [InlineData(ResumeTemplateCatalog.CompactTechnicalId)]
    public async Task PdfExport_RendersEveryTemplateWithCompactReferences(string templateId)
    {
        var bytes = await new PdfExporter().ExportAsync(CreateResume(templateId));

        Assert.True(bytes.Length > 1_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
        Assert.Contains("Target role: Platform Engineer", text);
        Assert.Contains("[1]", text);
        Assert.DoesNotContain("ref-1", text);
        Assert.Contains("/StructTreeRoot", System.Text.Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public async Task PdfExport_DoesNotRepeatCandidateIdentityOnLaterPages()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        for (var index = 0; index < 14; index++)
        {
            resume.Experience.Add(new WorkExperience
            {
                Company = $"Example Company {index + 2}",
                Title = "Platform Engineer",
                StartDate = new DateOnly(2010 + index, 1, 1),
                EndDate = new DateOnly(2010 + index, 12, 1),
                Achievements =
                [
                    "Designed and operated a production platform with observable delivery controls.",
                    "Improved reliability through automated testing and deployment validation."
                ]
            });
        }

        var bytes = await new PdfExporter().ExportAsync(resume);
        using var pdf = PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages > 1);
        var pageTexts = pdf.GetPages().Select(page => page.Text).ToList();
        Assert.Contains("Jane Smith", pageTexts[0]);
        Assert.DoesNotContain(pageTexts.Skip(1), text => text.Contains("Jane Smith", StringComparison.Ordinal));
        Assert.DoesNotContain(pageTexts, text => text.Contains(" / ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportedDocx_RoundTripPreservesRoleEmployerDateAssociations()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        resume.IncludeCompactJobMl = false;
        resume.MinimumOutputPages = 1;
        resume.Experience[0].Title = "Platform Engineer";
        resume.Experience[0].StartDate = new DateOnly(2022, 1, 1);
        resume.Experience[0].IsCurrent = true;
        resume.Experience.AddRange([
            new WorkExperience
            {
                Company = "Northstar Systems",
                Title = "Engineering Lead",
                StartDate = new DateOnly(2018, 3, 1),
                EndDate = new DateOnly(2021, 12, 1),
                Achievements = ["Led delivery of production services across a distributed team."]
            },
            new WorkExperience
            {
                Company = "Contoso Cloud",
                Title = "Senior Software Engineer",
                StartDate = new DateOnly(2014, 5, 1),
                EndDate = new DateOnly(2018, 2, 1),
                Achievements = ["Built and operated customer-facing APIs."]
            }
        ]);
        var path = Path.Combine(Path.GetTempPath(), $"lucidresume-conformance-{Guid.NewGuid():N}.docx");
        try
        {
            await File.WriteAllBytesAsync(path, await new DocxExporter().ExportAsync(resume));
            var parsed = Assert.IsType<ParsedDocument>(await new DocxDirectParser().ParseAsync(path));
            var roundTrip = ResumeDocument.Create("roundtrip.docx", "application/docx", parsed.PlainText.Length);
            MarkdownSectionParser.PopulateSections(roundTrip, parsed.Markdown, parsed.Sections);

            Assert.Collection(roundTrip.Experience,
                role => AssertRole(role, "Example Corp", "Platform Engineer", 2022),
                role => AssertRole(role, "Northstar Systems", "Engineering Lead", 2018),
                role => AssertRole(role, "Contoso Cloud", "Senior Software Engineer", 2014));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Paginated_exports_default_to_an_intentional_second_page()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        resume.IncludeCompactJobMl = false;

        var docxBytes = await new DocxExporter().ExportAsync(resume);
        using (var stream = new MemoryStream(docxBytes))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            Assert.Contains(document.MainDocumentPart!.Document!.Descendants<
                    DocumentFormat.OpenXml.Wordprocessing.Break>(),
                pageBreak => pageBreak.Type?.Value == DocumentFormat.OpenXml.Wordprocessing.BreakValues.Page);
        }

        var pdfBytes = await new PdfExporter().ExportAsync(resume);
        using var pdf = PdfDocument.Open(pdfBytes);
        Assert.True(pdf.NumberOfPages >= 2);
    }

    [Fact]
    public async Task Explicit_compact_layout_does_not_force_a_second_page()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        resume.IncludeCompactJobMl = false;
        resume.MinimumOutputPages = 1;

        var docxBytes = await new DocxExporter().ExportAsync(resume);
        using (var stream = new MemoryStream(docxBytes))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            Assert.DoesNotContain(document.MainDocumentPart!.Document!.Descendants<
                    DocumentFormat.OpenXml.Wordprocessing.Break>(),
                pageBreak => pageBreak.Type?.Value == DocumentFormat.OpenXml.Wordprocessing.BreakValues.Page);
        }

        var pdfBytes = await new PdfExporter().ExportAsync(resume);
        using var pdf = PdfDocument.Open(pdfBytes);
        Assert.Equal(1, pdf.NumberOfPages);
    }

    [Fact]
    public async Task Human_exports_keep_selected_engineering_before_employment_history()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        resume.Projects.Add(new Project
        {
            Name = "Evidence Compiler",
            Description = "Compiled reviewed evidence into a role-specific projection."
        });

        var docx = await new DocxExporter().ExportAsync(resume);
        using var docxStream = new MemoryStream(docx);
        using var document = WordprocessingDocument.Open(docxStream, false);
        var docxText = document.MainDocumentPart!.Document!.Body!.InnerText;
        var pdf = await new PdfExporter().ExportAsync(resume);
        using var pdfDocument = PdfDocument.Open(pdf);
        var pdfText = string.Join("\n", pdfDocument.GetPages().Select(page => page.Text));

        Assert.True(docxText.IndexOf("Selected Engineering", StringComparison.Ordinal) <
                    docxText.IndexOf("Experience", StringComparison.Ordinal));
        Assert.True(pdfText.IndexOf("Selected Engineering", StringComparison.Ordinal) <
                    pdfText.IndexOf("Experience", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkdownExport_ReturnsOnePassParseableCJobMlProjection()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);

        var bytes = await new MarkdownExporter().ExportAsync(resume);
        var markdown = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("# Jane Smith", markdown);
        Assert.Contains("[[1]](#ref-1)", markdown);
        Assert.Contains("## References", markdown);
        Assert.Contains("cJobML 0.1", markdown);
        Assert.DoesNotContain("```jobml", markdown);
        Assert.True(CJobMlParser.TryParse(markdown, out var compact, out var error), error);
        Assert.Single(compact!.References);
    }

    [Fact]
    public async Task MarkdownExport_PreservesFullJobMlWhenCompactProjectionWouldBeEmpty()
    {
        const string markdown = "# Jane Smith\n\n## Experience\n\nBuilt a reliable platform for customers.";
        var file = JobMlDraftGenerator.Generate(markdown);
        Assert.All(file.Data.Claims, claim => claim.Review = "accepted");
        var source = JobMlArtifactComposer.Compose(file);
        var resume = ResumeDocument.Create("jane.md", "text/markdown", source.Length);
        resume.JobMlSource = source;

        var exported = System.Text.Encoding.UTF8.GetString(
            await new MarkdownExporter().ExportAsync(resume));

        Assert.Contains("## MACHINE AREA", exported);
        Assert.Contains("```jobml", exported);
        Assert.True(new JobMlParser().TryParse(exported, out _, out var error), error);
    }

    [Fact]
    public async Task MarkdownExport_CanOmitCompactJobMlForHumanOnlyCopy()
    {
        var resume = CreateResume(ResumeTemplateCatalog.AtsClassicId);
        resume.IncludeCompactJobMl = false;

        var markdown = System.Text.Encoding.UTF8.GetString(
            await new MarkdownExporter().ExportAsync(resume));

        Assert.Equal(resume.CanonicalMarkdown, markdown);
        Assert.DoesNotContain("## References", markdown);
        Assert.DoesNotContain("MACHINE AREA", markdown);
    }

    [Fact]
    public async Task Human_exports_render_contact_preference_after_phone_without_corrupting_phone_value()
    {
        var resume = ResumeDocument.Create("contact.md", "text/markdown", 0);
        resume.Personal.FullName = "Scott Galloway";
        resume.Personal.Email = "scott@mostlylucid.net";
        resume.Personal.Phone = "07498479614";
        resume.Personal.ContactPreference = "please email in the first instance";

        var markdown = System.Text.Encoding.UTF8.GetString(await new MarkdownExporter().ExportAsync(resume));
        var docx = await new DocxExporter().ExportAsync(resume);
        var pdf = await new PdfExporter().ExportAsync(resume);
        using var stream = new MemoryStream(docx);
        using var document = WordprocessingDocument.Open(stream, false);
        using var pdfDocument = PdfDocument.Open(pdf);
        var pdfText = string.Join("\n", pdfDocument.GetPages().Select(page => page.Text));

        Assert.Contains("scott@mostlylucid.net", markdown);
        Assert.Contains("07498479614 - please email in the first instance -", markdown);
        Assert.Contains("07498479614 - please email in the first instance -",
            document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Contains("07498479614 - please email in the first instance -", pdfText);
        Assert.Equal("07498479614", resume.Personal.Phone);
    }

    [Fact]
    public void ArtifactBuilder_CreatesReversibleEvidenceBackedOutput()
    {
        var source = ResumeDocument.Create("source.md", "text/markdown", 1);
        source.Personal.FullName = "Jane Smith";
        source.Personal.Email = "jane@example.com";
        source.CompleteJobMlUri = "https://example.com/jane.jobml";
        source.Skills.Add(new Skill { Name = "Kubernetes" });
        var experience = new WorkExperience
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Company = "Example Corp",
            Title = "Platform Engineer",
            StartDate = new DateOnly(2022, 1, 1),
            IsCurrent = true,
            Achievements = ["Operated Kubernetes workloads in production."]
        };
        source.Experience.Add(experience);
        var project = new Project
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Atlas",
            Description = "Production orchestration platform",
            Technologies = ["Kubernetes"],
            Url = "https://github.com/example/atlas"
        };
        source.Projects.Add(project);
        var ledger = EvidenceLedgerBuilder.Rebuild(source);

        var generated = ResumeDocument.Create("projected.md", "text/markdown", 1);
        generated.RawMarkdown = $"# Jane Smith\n\n## Experience {{#experience}}\n\n### Platform Engineer - Example Corp {{#experience-{experience.Id:N}}}\n\nOperated Kubernetes workloads in production.\n\n## Projects {{#projects}}\n\nAtlas: Production orchestration platform";
        generated.Personal = source.Personal;
        generated.Experience.Add(experience);
        generated.Projects.Add(project);
        generated.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = source.ResumeId,
            SourceRevision = ledger.SourceRevision,
            Blocks =
            [
                new ResumeProjectionBlock
                {
                    ClaimId = ledger.Claims.Single(claim => claim.Id.EndsWith(":achievement:1")).Id,
                    ProseRef = $"#experience-{experience.Id:N}:p1"
                },
                new ResumeProjectionBlock
                {
                    ClaimId = ledger.Claims.Single(claim => claim.Id.EndsWith($"project:{project.Id:N}:description")).Id,
                    ProseRef = "#projects:p1"
                }
            ]
        };
        var job = JobDescription.Create("Kubernetes platform role", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Platform Engineer";
        job.RequiredSkills.Add("Kubernetes");

        var artifact = new ResumeArtifactBuilder().Build(
            source, generated, job, ResumeTemplateCatalog.ModernProfessionalId);

        Assert.Equal("Jane Smith", artifact.Personal.FullName);
        Assert.Contains("## MACHINE AREA", artifact.JobMlSource);
        Assert.Contains("https://github.com/example/atlas", artifact.JobMlSource);
        Assert.Contains("fingerprint:", artifact.JobMlSource);
        Assert.Contains("type: career_transcript", artifact.JobMlSource);
        Assert.Contains("ledger://evidence:", artifact.JobMlSource);
        var projectedExperience = Assert.Single(artifact.Experience);
        Assert.Equal("Example Corp", projectedExperience.Company);
        Assert.Equal("Platform Engineer", projectedExperience.Title);
        Assert.Null(projectedExperience.Location);
        Assert.Equal("jane@example.com", artifact.Personal.Email);
        Assert.Contains("importance: required", artifact.JobMlSource);
        Assert.DoesNotContain("\n    all:", artifact.JobMlSource);
        Assert.DoesNotContain("MACHINE AREA", artifact.CanonicalMarkdown);
        Assert.Equal(job.JobId, artifact.TailoredForJobId);
        Assert.Equal("Platform Engineer", artifact.TargetRole);
        Assert.True(new JobMlParser().TryParse(artifact.JobMlSource!, out var parsed, out var parseError), parseError);
        var compact = CJobMlProjector.Project(parsed!);
        Assert.Contains("[Career Transcript]", compact.Markdown);
        Assert.Contains("Full JobML: <https://example.com/jane.jobml>", compact.Markdown);
        Assert.Contains("<https://example.com/jane.jobml#entity-experience", compact.Markdown);
        Assert.DoesNotContain("ledger://", compact.Markdown);
        Assert.DoesNotContain("fingerprint", compact.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(JobMlProcessor.Validate(parsed!), diagnostic =>
            diagnostic.Severity == JobMlDiagnosticSeverity.Error);
    }

    [Fact]
    public void ArtifactBuilder_RejectsProjectionAfterLedgerDrift()
    {
        var source = ResumeDocument.Create("source.md", "text/markdown", 1);
        source.Personal.Summary = "Original evidence.";
        var ledger = EvidenceLedgerBuilder.Rebuild(source);
        var projection = ResumeDocument.Create("projection.md", "text/markdown", 1);
        projection.RawMarkdown = "# Jane\n\n## Summary {#summary}\n\nOriginal evidence.";
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = source.ResumeId,
            SourceRevision = ledger.SourceRevision,
            Blocks =
            [
                new ResumeProjectionBlock
                {
                    ClaimId = ledger.Claims.Single().Id,
                    ProseRef = "#summary:p1"
                }
            ]
        };
        source.Personal.Summary = "Changed evidence.";

        var error = Assert.Throws<InvalidOperationException>(() => new ResumeArtifactBuilder().Build(
            source, projection,
            JobDescription.Create("Role", new JobSource { Type = JobSourceType.PastedText }),
            ResumeTemplateCatalog.AtsClassicId));

        Assert.Contains("ledger changed", error.Message);
    }

    private static void AssertRole(WorkExperience role, string company, string title, int startYear)
    {
        Assert.Equal(company, role.Company);
        Assert.Equal(title, role.Title);
        Assert.Equal(startYear, role.StartDate?.Year);
    }

    private static ResumeDocument CreateResume(string templateId)
    {
        const string markdown = "# Jane Smith\n\n## Experience\n\n### Engineer at Example Corp\n\nBuilt a reliable platform for customers.";
        var file = JobMlDraftGenerator.Generate(markdown);
        file.Data.Document.FullJobMl = "https://example.com/jane.jobml";
        var claim = Assert.Single(file.Data.Claims);
        claim.Review = "accepted";
        claim.Evidence.Add(new JobMlEvidence
        {
            Id = "reduced-rag",
            Type = "article",
            Uri = "https://mostlylucid.net/reduced-rag",
            Title = "Reduced RAG",
            Authors = ["S. Galloway"],
            Publisher = "MostlyLucid",
            Published = "2025-04-12"
        });
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
        resume.TargetRole = "Platform Engineer";
        return resume;
    }
}
