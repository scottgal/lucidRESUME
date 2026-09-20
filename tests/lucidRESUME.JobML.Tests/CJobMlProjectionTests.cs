using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class CJobMlProjectionTests
{
    [Fact]
    public void Projection_IsCompactJatsLikeAndRoundTripsThroughOnePassParser()
    {
        var full = AcceptedFile();

        var compact = CJobMlProjector.Project(full);
        var parsed = CJobMlParser.Parse(compact.Markdown);

        Assert.Contains("platform. [[1]](#ref-1)", compact.Markdown);
        Assert.Contains("## References", compact.Markdown);
        Assert.Contains("cJobML 0.1: xref [n] in prose resolves to ref [n].", compact.Markdown);
        Assert.Contains("Full JobML: <https://example.com/jane.jobml>", compact.Markdown);
        Assert.Contains("[Article] <https://mostlylucid.net/reduced-rag>", compact.Markdown);
        Assert.DoesNotContain("fingerprint", compact.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selector", compact.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```jobml", compact.Markdown);

        Assert.Equal([1], parsed.Xrefs);
        var reference = Assert.Single(parsed.References);
        Assert.Equal("Reduced RAG", reference.Title);
        Assert.Equal("Article", reference.Type);
        Assert.Equal("https://mostlylucid.net/reduced-rag", reference.Uri!.ToString().TrimEnd('/'));
        Assert.Equal("https://example.com/jane.jobml", parsed.CompleteLedger!.ToString().TrimEnd('/'));
    }

    [Fact]
    public void Projection_DeduplicatesEvidenceAndReusesNumber()
    {
        var full = AcceptedFile();
        var second = JobMlDraftGenerator.Generate("# Jane\n\n## Experience\n\nBuilt a second evidence-linked system.");
        var secondClaim = Assert.Single(second.Data.Claims);
        secondClaim.Review = "accepted";
        secondClaim.Id = "projects-claim-1";
        secondClaim.Evidence.Add(full.Data.Claims[0].Evidence[1]);
        full = full with
        {
            Markdown = full.Markdown + "\n\n## Projects {#projects}\n\nBuilt a second evidence-linked system."
        };
        secondClaim.Subject = "projects";
        secondClaim.Evidence[0].Ref = "#projects:p1";
        secondClaim.Evidence[0].Fingerprint = new JobMlFingerprint
        {
            Text = MarkdownEvidenceIndex.Fingerprint("Built a second evidence-linked system.")
        };
        secondClaim.Evidence[0].Selector = new JobMlTextSelector { Exact = "Built a second evidence-linked system." };
        full.Data.Entities.Add(new JobMlEntity { Id = "projects", Type = "project", Name = "Projects", Source = "#projects" });
        full.Data.Claims.Add(secondClaim);

        var compact = CJobMlProjector.Project(full);

        Assert.Single(compact.References);
        Assert.Equal(2, compact.Anchors.Count);
        Assert.All(compact.Anchors, anchor => Assert.Equal([1], anchor.ReferenceNumbers));
    }

    [Fact]
    public void Projection_DoesNotPublishUnreviewedDerivedClaims()
    {
        var full = AcceptedFile();
        full.Data.Claims[0].Review = "required";

        var compact = CJobMlProjector.Project(full);

        Assert.Empty(compact.References);
        Assert.DoesNotContain("## References", compact.Markdown);
    }

    [Fact]
    public void Projection_PublishesCompleteLedgerEndpointWithoutExternalReferences()
    {
        var full = AcceptedFile();
        full.Data.Claims.Single().Evidence.RemoveAll(evidence =>
            !string.Equals(evidence.Type, "prose", StringComparison.OrdinalIgnoreCase));

        var compact = CJobMlProjector.Project(full);

        Assert.Empty(compact.References);
        Assert.Contains("## References", compact.Markdown);
        Assert.Contains("Full JobML: <https://example.com/jane.jobml>", compact.Markdown);
        var parsed = CJobMlParser.Parse(compact.Markdown);
        Assert.Empty(parsed.References);
        Assert.Equal("https://example.com/jane.jobml", parsed.CompleteLedger?.ToString());
    }

    [Fact]
    public void Parser_RejectsUnresolvedXref()
    {
        var compact = CJobMlProjector.Project(AcceptedFile()).Markdown
            .Replace("[[1]](#ref-1)", "[[2]](#ref-2)", StringComparison.Ordinal);

        var error = Assert.Throws<CJobMlParseException>(() => CJobMlParser.Parse(compact));

        Assert.Contains("Unresolved", error.Message);
    }

    [Fact]
    public void Parser_RejectsReferenceWhichIsNotCited()
    {
        var compact = CJobMlProjector.Project(AcceptedFile()).Markdown
            .Replace("platform. [[1]](#ref-1)", "platform.", StringComparison.Ordinal);

        var error = Assert.Throws<CJobMlParseException>(() => CJobMlParser.Parse(compact));

        Assert.Contains("Uncited", error.Message);
    }

    [Fact]
    public void Parser_UsesFinalExactReferencesHeading()
    {
        var file = AcceptedFile() with
        {
            Markdown = AcceptedFile().Markdown.Replace("## Experience", "## References in prior work\n\nContext.\n\n## Experience",
                StringComparison.Ordinal)
        };

        var compact = CJobMlProjector.Project(file).Markdown;
        var parsed = CJobMlParser.Parse(compact);

        Assert.Single(parsed.References);
    }

    private static JobMlFile AcceptedFile()
    {
        var file = JobMlDraftGenerator.Generate(
            "# Jane\n\n## Experience\n\nBuilt an evidence-linked retrieval platform.");
        file.Data.Document.CompleteLedger = "https://example.com/jane.jobml";
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
        return file;
    }
}
