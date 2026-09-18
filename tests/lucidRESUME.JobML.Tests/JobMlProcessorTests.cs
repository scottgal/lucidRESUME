using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class JobMlProcessorTests
{
    private const string Prose = """
        # Jane Smith

        ## Experience

        ### Example Corp {#example-corp}

        Led the modernisation of a high-volume ASP.NET Core platform.
        """;

    [Fact]
    public void Parser_RoundTripsEmbeddedJobMl()
    {
        var parser = new JobMlParser();
        var generated = JobMlDraftGenerator.Generate(Prose);

        var serialized = parser.Serialize(generated);
        var reparsed = parser.Parse(serialized);

        Assert.Equal("0.1", reparsed.Data.Version);
        Assert.Single(reparsed.Data.Entities);
        Assert.Single(reparsed.Data.Claims);
        Assert.Contains("```jobml", serialized);
        Assert.Contains("jobml:\n  version: 0.1", serialized.ReplaceLineEndings("\n"));
        Assert.Contains("semantics:", serialized);
        Assert.Contains("Do not infer unsupported claims", serialized);
        Assert.DoesNotContain("```jobml", reparsed.Markdown);
    }

    [Fact]
    public void Parser_RoundTripsYamlIndependentlyForSplitEditor()
    {
        var parser = new JobMlParser();
        var generated = JobMlDraftGenerator.Generate(Prose);

        var yaml = parser.SerializeYaml(generated.Data);
        var reparsed = parser.ParseYaml(yaml);

        Assert.Equal(generated.Data.Document.Id, reparsed.Document.Id);
        Assert.Equal(generated.Data.Claims[0].Id, reparsed.Claims[0].Id);
        Assert.DoesNotContain("```jobml", yaml);
    }

    [Fact]
    public void Parser_PreservesNamespacedExtensionPayloads()
    {
        var parser = new JobMlParser();
        var generated = JobMlDraftGenerator.Generate(Prose);
        generated.Data.Extensions = new Dictionary<string, object?>
        {
            ["lucidresume.github"] = new Dictionary<string, object?>
            {
                ["version"] = "0.1",
                ["repositories"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "github-example-platform",
                        ["uri"] = "https://github.com/example/platform",
                        ["revision"] = "0123456789abcdef"
                    }
                }
            }
        };

        var serialized = parser.Serialize(generated);
        var reparsed = parser.Parse(serialized);
        var roundTripped = parser.Serialize(reparsed);

        Assert.NotNull(reparsed.Data.Extensions);
        Assert.Contains("lucidresume.github:", roundTripped);
        Assert.Contains("revision: 0123456789abcdef", roundTripped);
        Assert.Contains("https://github.com/example/platform", roundTripped);
    }

    [Fact]
    public void Parser_UpgradesLegacyScalarHeaderAndEmitsSelfDescribingHeader()
    {
        var source = """
            # Resume

            ```jobml
            jobml: "0.1"
            document:
              id: resume
              language: en-GB
            entities: []
            claims: []
            concepts: []
            ```
            """;
        var parser = new JobMlParser();

        var parsed = parser.Parse(source);
        var upgraded = parser.Serialize(parsed);

        Assert.Equal("0.1", parsed.Data.Version);
        Assert.Equal(JobMlHeader.DefaultSemantics, parsed.Data.Header.Semantics);
        Assert.Contains("purpose:", upgraded);
        Assert.Contains("Treat JobML as a higher-resolution description", upgraded);
    }

    [Fact]
    public void GeneratedDocument_PassesStructuralColdParserContract()
    {
        var serialized = new JobMlParser().Serialize(JobMlDraftGenerator.Generate(Prose));

        Assert.Contains("claims:", serialized);
        Assert.Contains("statement:", serialized);
        Assert.Contains("supported_by:", serialized);
        Assert.Contains("ref:", serialized);
        Assert.Contains("fingerprint:", serialized);
        Assert.Contains("human-readable prose", serialized);
        Assert.Contains("Do not infer unsupported claims", serialized);
    }

    [Fact]
    public void Parser_UpgradesLegacyEvidenceKey()
    {
        var parser = new JobMlParser();
        var current = parser.Serialize(JobMlDraftGenerator.Generate(Prose));
        var legacy = current.Replace("supported_by:", "evidence:", StringComparison.Ordinal);

        var parsed = parser.Parse(legacy);
        var upgraded = parser.Serialize(parsed);

        Assert.Single(parsed.Data.Claims[0].Evidence);
        Assert.Contains("supported_by:", upgraded);
        Assert.DoesNotContain("\n    evidence:", upgraded);
    }

    [Fact]
    public void ColdParserProbe_SeparatesBlindQuestionsFromDeterministicGroundTruth()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        var document = new JobMlParser().Serialize(file);

        var probe = JobMlColdParserProbe.Create(document, file);

        Assert.Equal(document, probe.Document);
        Assert.Contains(probe.Questions, question => question.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        var expected = Assert.Single(probe.GroundTruth);
        Assert.Equal(file.Data.Claims[0].Statement, expected.Statement);
        Assert.Equal("valid", Assert.Single(expected.Evidence).State);
        Assert.Contains("Led the modernisation", expected.Evidence[0].Prose);
    }

    [Fact]
    public void GeneratedEvidence_IsValidButDerivedClaimRequiresReview()
    {
        var file = JobMlDraftGenerator.Generate(Prose);

        var evidence = JobMlProcessor.Reconcile(file);
        var diagnostics = JobMlProcessor.Validate(file);

        Assert.Equal(EvidenceState.Valid, Assert.Single(Assert.Single(evidence).Evidence).State);
        Assert.StartsWith("fnv1a64:", file.Data.Claims[0].Evidence[0].Fingerprint!.Text);
        Assert.Contains(diagnostics, d => d.Code == "JML024" && d.Severity == JobMlDiagnosticSeverity.Warning);
        Assert.DoesNotContain(diagnostics, d => d.Severity == JobMlDiagnosticSeverity.Error);
    }

    [Fact]
    public void EditingReferencedProse_ReportsChanged()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        file = file with { Markdown = file.Markdown.Replace("Led the modernisation", "Contributed to the modernisation") };

        var resolution = Assert.Single(Assert.Single(JobMlProcessor.Reconcile(file)).Evidence);

        Assert.Equal(EvidenceState.Changed, resolution.State);
        Assert.Contains("Contributed", resolution.CurrentText);
    }

    [Fact]
    public void EvidencePassage_TracksExactEditorSourceSpan()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        var passage = Assert.Single(MarkdownEvidenceIndex.Create(file.Markdown).Passages);

        var selectedSource = file.Markdown.Substring(passage.SourceStart, passage.SourceLength);

        Assert.Contains("Led the modernisation", selectedSource);
        Assert.Equal(passage.Text, MarkdownEvidenceIndex.NormalizeText(selectedSource));
    }

    [Fact]
    public void QuoteSelectorWithoutFingerprint_StillDetectsDrift()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        file.Data.Claims[0].Evidence[0].Fingerprint = null;
        file = file with { Markdown = file.Markdown.Replace("Led the modernisation", "Contributed to the modernisation") };

        var resolution = Assert.Single(Assert.Single(JobMlProcessor.Reconcile(file)).Evidence);

        Assert.Equal(EvidenceState.Changed, resolution.State);
    }

    [Fact]
    public void AcceptedProseEvidence_RequiresDriftFingerprint()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        file.Data.Claims[0].Review = "accepted";
        file.Data.Claims[0].Evidence[0].Fingerprint = null;

        var diagnostics = JobMlProcessor.Validate(file);

        Assert.Contains(diagnostics, d => d.Code == "JML027" && d.Severity == JobMlDiagnosticSeverity.Error);
    }

    [Fact]
    public void MovingPassage_RecoversAUniqueSuggestedReference()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        var evidence = file.Data.Claims[0].Evidence[0];
        evidence.Ref = "#old-heading:p1";

        var resolution = Assert.Single(Assert.Single(JobMlProcessor.Reconcile(file)).Evidence);

        Assert.Equal(EvidenceState.Changed, resolution.State);
        Assert.Equal("#example-corp:p1", resolution.SuggestedReference);
    }

    [Fact]
    public void DuplicateRelocatedPassage_IsAmbiguousWithoutContextMatch()
    {
        var markdown = Prose + "\n\n### Other Corp {#other-corp}\n\nLed the modernisation of a high-volume ASP.NET Core platform.\n";
        var file = JobMlDraftGenerator.Generate(markdown);
        var evidence = file.Data.Claims[0].Evidence[0];
        evidence.Ref = "#missing:p1";
        evidence.Selector!.Prefix = null;
        evidence.Selector.Suffix = null;

        var resolution = Assert.Single(JobMlProcessor.Reconcile(file)[0].Evidence);

        Assert.Equal(EvidenceState.Ambiguous, resolution.State);
    }

    [Fact]
    public void Coverage_OnlyCountsAcceptedClaimsWithValidEvidence()
    {
        var file = JobMlDraftGenerator.Generate(Prose);
        file.Data.Concepts.Add(new JobMlConcept
        {
            Id = "aspnet-core", Type = "skill", Name = "ASP.NET Core", Aliases = ["ASP.NET"]
        });
        file.Data.Claims[0].Concepts.Skills.Add("aspnet-core");
        file.Data.Requirements.Add(new JobMlRequirement
        {
            Id = "req-aspnet", Concept = "ASP.NET", Importance = "required"
        });

        var beforeReview = Assert.Single(JobMlProcessor.AnalyseCoverage(file));
        file.Data.Claims[0].Review = "accepted";
        var afterReview = Assert.Single(JobMlProcessor.AnalyseCoverage(file));

        Assert.Equal(CoverageState.Ambiguous, beforeReview.State);
        Assert.Equal(CoverageState.Direct, afterReview.State);
    }

    [Fact]
    public void Parser_RejectsMultipleJobMlBlocks()
    {
        var source = "# Resume\n\n```jobml\njobml: '0.1'\n```\n\n```jobml\njobml: '0.1'\n```";

        var exception = Assert.Throws<JobMlParseException>(() => new JobMlParser().Parse(source));

        Assert.Contains("exactly one", exception.Message);
    }
}
