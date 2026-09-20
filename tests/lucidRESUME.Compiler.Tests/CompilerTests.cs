using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Compiler.Tests;

public sealed class CompilerTests
{
    [Fact]
    public async Task Snapshot_store_rejects_drift_and_publishes_immutable_revision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucidresume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemJobMlSnapshotStore(Options.Create(new JobMlCompilerOptions
                { SnapshotDirectory = directory }));
            var snapshot = await store.PublishAsync(Fixture.Source);
            var current = await store.GetCurrentAsync();
            var versioned = await store.GetAsync(snapshot.Revision);

            Assert.Equal(64, snapshot.Revision.Length);
            Assert.Equal(snapshot.Revision, current?.Revision);
            Assert.Equal(snapshot.Source, versioned?.Source);
            await Assert.ThrowsAsync<JobMlPublicationException>(() =>
                store.PublishAsync(Fixture.Source.Replace("15 engineer", "50 engineer")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Compiler_selects_human_master_prose_and_reports_real_gap()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            Fixture.Source, file, JobMlProcessor.Validate(file));
        var orchestrator = new ResumeCompositionOrchestrator([], new CompositionValidator());
        var compiler = new JobMlResumeCompiler(new FakeJobParser(), orchestrator);

        var result = await compiler.CompileAsync(snapshot,
            "VP Engineering. Must have TypeScript and AWS. Terraform is required.");

        Assert.Contains("Led a 15 engineer", result.HumanMarkdown);
        Assert.Contains("Terraform", result.Manifest.Gaps);
        Assert.DoesNotContain("Terraform", result.HumanMarkdown);
        Assert.Contains("cJobML 0.1", result.PublishedMarkdown);
        Assert.Contains("complete.example/ledger", result.FullJobMlMarkdown);
    }

    [Fact]
    public async Task Orchestrator_discards_a_pass_that_invents_a_number()
    {
        var claim = new JobMlClaim { Id = "leadership", Subject = "role", Statement = "Led engineering." };
        var selected = new SelectedClaim(claim, "Example", "Led a 15 engineer team.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 80, [selected], []);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new InventingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "lead a team", new CompilationOptions
            { ComposeProse = true, CompositionProvider = "bad" });

        Assert.Equal("Led a 15 engineer team.", result.Blocks.Single().Text);
        Assert.Contains(result.Warnings, x => x.Contains("numeric fact '40'"));
    }

    [Fact]
    public async Task Orchestrator_discards_an_unsupported_term_copied_from_the_vacancy()
    {
        var claim = new JobMlClaim { Id = "leadership", Subject = "role", Statement = "Led engineering." };
        var selected = new SelectedClaim(claim, "Example", "Led an engineering team.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 80, [selected], []);
        var requirement = new CompilerRequirement("req-1", "Terraform experience", RequirementKind.Required, "Terraform experience");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new VacancyCopyingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "Terraform experience", new CompilationOptions
            { ComposeProse = true, CompositionProvider = "copy" });

        Assert.Equal("Led an engineering team.", result.Blocks.Single().Text);
        Assert.Contains(result.Warnings, x => x.Contains("target-role term 'terraform'"));
    }

    private sealed class FakeJobParser : IJobSpecParser
    {
        public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new JobDescription
            {
                RawText = text,
                RequiredSkills = ["TypeScript", "AWS", "Terraform"],
                Responsibilities = ["Lead engineering teams through change"]
            });
        public Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class InventingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "bad";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionDraft(
                request.CurrentBlocks.Select(x => x with { Text = "Led a 40 engineer team." }).ToList(), []));
    }

    private sealed class VacancyCopyingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "copy";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionDraft(
                request.CurrentBlocks.Select(x => x with { Text = "Led an engineering and Terraform team." }).ToList(), []));
    }
}

internal static class Fixture
{
    private const string Prose = "Led a 15 engineer TypeScript team through platform change on AWS, with accountable release and security governance.";
    private static readonly string Fingerprint = MarkdownEvidenceIndex.Fingerprint(Prose);

    public static string Source => $$"""
        # Alex Example

        ## Complete Experience

        ### Example Ltd {#example-role}

        <p id="example-leadership">
        {{Prose}}
        </p>

        ---

        ```jobml
        jobml:
          version: "0.1"
          purpose: Complete machine-readable evidence ledger for this resume.
          semantics:
            - Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.
            - Every substantive claim should be supported by one or more evidence references.
            - Do not infer unsupported claims.
        document:
          id: alex-complete-resume
          language: en-GB
          complete_ledger: https://complete.example/ledger
        entities:
          - id: example-role
            type: experience
            name: VP Engineering, Example Ltd
            source: "#example-role"
        claims:
          - id: leadership
            subject: example-role
            statement: Led an engineering team through platform change.
            review: accepted
            concepts:
              skills: [typescript, aws]
              capabilities: [engineering-leadership]
            supported_by:
              - id: leadership-prose
                type: prose
                ref: "#example-leadership"
                fingerprint:
                  text: "{{Fingerprint}}"
              - id: engineering-post
                type: article
                uri: https://example.com/engineering
                title: Engineering through change
        concepts:
          - id: typescript
            type: skill
            name: TypeScript
          - id: aws
            type: skill
            name: AWS
            aliases: [Amazon Web Services]
          - id: engineering-leadership
            type: capability
            name: Engineering Leadership
            aliases: [lead engineering teams]
        ```
        """;
}
