using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using UglyToad.PdfPig;

namespace lucidRESUME.Core.Tests;

public sealed class GeneratedResumeArchiveTests
{
    [Fact]
    public void Complete_career_keeps_original_source_identity_and_review_decisions()
    {
        var first = ResumeDocument.Create("original.docx", "application/docx", 0);
        first.Personal.Summary = "An author-confirmed engineering story.";
        first.Experience.Add(new WorkExperience { Title = "Engineer", Company = "Original", Achievements = ["Built auditable recovery."] });
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(first);
        ledger.Claims.Single(item => item.Kind == "achievement").Review = "rejected";
        var second = ResumeDocument.Create("other.md", "text/markdown", 0);
        var aggregate = new AppState { Resumes = [first, second], SelectedResumeId = second.ResumeId }.BuildAggregateResume()!;
        var claim = Assert.Single(aggregate.EvidenceLedger.Claims, item => item.Kind == "achievement");
        Assert.Equal("rejected", claim.Review);
        Assert.All(claim.EvidenceIds, id => Assert.Equal("original.docx", aggregate.EvidenceLedger.Evidence.Single(item => item.Id == id).SourceName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_survives_restart_and_keeps_original_files_and_citation_evidence(bool sqlite)
    {
        var root = Path.Combine(Path.GetTempPath(), "lucid-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        IAppStore OpenStore() => sqlite ? new SqliteAppStore(Path.Combine(root, "state.db")) : new JsonAppStore(Path.Combine(root, "state.json"));
        try
        {
            var source = ResumeDocument.Create("original.md", "text/markdown", 0);
            source.Personal.FullName = "Example Candidate";
            var role = new WorkExperience { Title = "Engineer", Company = "Original company", Achievements = ["Built reliable APIs with audited recovery."] };
            source.Experience.Add(role);
            var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
            var claim = ledger.Claims.Single(item => item.Kind == "achievement");
            var projection = ResumeDocument.Create("projected.md", "text/markdown", 0);
            projection.Personal.FullName = source.Personal.FullName;
            projection.Experience.Add(role);
            projection.RawMarkdown = "## Experience {#role}\n\nBuilt reliable APIs with audited recovery.";
            projection.Projection = new ResumeProjectionInfo
            {
                SourceResumeId = source.ResumeId,
                SourceRevision = ledger.SourceRevision,
                Blocks = [new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = "#role:p1" }]
            };
            var job = new JobDescription { Title = "Platform Engineer", Company = "Target company", RawText = "Reliable APIs" };
            var artifact = new ResumeArtifactBuilder().Build(source, projection, job, "ats-classic");
            GeneratedResume saved;
            byte[] pdfBytes;
            using (var store = (IDisposable)OpenStore())
            {
                var archive = new GeneratedResumeArchive((IAppStore)store, [new DocxExporter(), new PdfExporter()], new(root));
                saved = await archive.SaveAsync(source, artifact, job);
                role.Achievements[0] = "A later account that must not overwrite history.";
                job.Company = "Different target";
                artifact.Personal.FullName = "Changed name";
                Assert.Contains("audited recovery", saved.Source.Experience[0].Achievements[0]);
                Assert.Equal("Target company", saved.Job.Company);
                Assert.Equal("Example Candidate", saved.Resume.Personal.FullName);
                pdfBytes = await File.ReadAllBytesAsync(archive.FileFor(saved.Id, "pdf"));
                using var pdf = PdfDocument.Open(pdfBytes);
                Assert.Equal(pdf.NumberOfPages, archive.PdfPages(saved.Id).Length);
                Assert.True(File.Exists(archive.FileFor(saved.Id, "docx")));
                Assert.Empty((await ((IAppStore)store).LoadAsync()).Resumes);
            }
            using (var store = (IDisposable)OpenStore())
            {
                var reopened = Assert.Single((await ((IAppStore)store).LoadAsync()).GeneratedResumes);
                var archive = new GeneratedResumeArchive((IAppStore)store, [new DocxExporter(), new PdfExporter()], new(root));
                await archive.EnsureFilesAsync(reopened);
                Assert.Equal(pdfBytes, await File.ReadAllBytesAsync(archive.FileFor(saved.Id, "pdf")));
                var evidence = ArchivedEvidenceResolver.Resolve(reopened);
                Assert.Contains(evidence, item => item.Markdown.Contains("original.md") && item.Markdown.Contains("audited recovery"));
                Assert.DoesNotContain(evidence, item => item.Markdown.Contains("A later account"));
                using var exported = new MemoryStream();
                await ((IAppStore)store).ExportJsonAsync(exported);
                using var jsonStore = new JsonAppStore(Path.Combine(root, "portable.json"));
                exported.Position = 0;
                await jsonStore.ImportJsonAsync(exported);
                Assert.Equal(reopened.Id, Assert.Single((await jsonStore.LoadAsync()).GeneratedResumes).Id);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
