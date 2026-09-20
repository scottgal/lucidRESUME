using System.Net;
using lucidRESUME.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using lucidRESUME.Compiler;
using lucidRESUME.JobML;

namespace lucidRESUME.AI.Tests;

public sealed class LlamaSharpIntegrationTests
{
    [Fact]
    public async Task InstalledGrugModel_PerformsBoundedSingleSectionEdit()
    {
        var options = Options.Create(new LlamaSharpOptions { MaxTokens = 1_400, Temperature = 0.1f });
        var manager = new LlamaSharpModelManager(new HttpClient(), options,
            NullLogger<LlamaSharpModelManager>.Instance);
        if (!manager.IsModelPresent) return;

        using var runtime = new LlamaSharpRuntime(options, manager, NullLogger<LlamaSharpRuntime>.Instance);
        var provider = new LlamaSharpResumeCompositionProvider(runtime);
        var claim = new JobMlClaim
        {
            Id = "claim-1", Subject = "role-1",
            Statement = "Led a 10-person engineering team through platform change."
        };
        const string prose = "Led a 10-person engineering team through platform change, while remaining hands-on with C#.";
        var selected = new SelectedClaim(claim, "Example Ltd", prose, ["evidence-1"], .95, []);
        var requirement = new CompilerRequirement("req-1", "engineering leadership", RequirementKind.Required,
            "engineering leadership");
        var packet = new EvidencePacket("role-1", "Example Ltd", "Tighten", 30, [selected], ["req-1"]);
        var block = new CompositionBlock("role-1", prose, [claim.Id], ["evidence-1"]);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "onnx");

        var validator = new CompositionValidator();
        var orchestrator = new ResumeCompositionOrchestrator([provider], validator);
        var result = await orchestrator.ComposeAsync(manifest, "ignored vacancy", new CompilationOptions
        {
            ComposeProse = true,
            CompositionProvider = provider.ProviderId
        });

        // Local model output is sampled and may be rejected. The product-level
        // invariant is that the accepted result is valid, with source prose used
        // as a safe fallback whenever either editing pass leaks unsupported terms.
        var accepted = new CompositionDraft(result.Blocks, []);
        Assert.Empty(validator.Validate(accepted, [block], manifest));
        Assert.DoesNotContain('—', Assert.Single(result.Blocks).Text);
        if (!result.Used) Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void RemoveThinking_ReturnsOnlyFinalAnswer()
    {
        var result = LlamaSharpRuntime.RemoveThinking(
            "<think>I should not be shown.</think>\n# Resume\n\nEvidence-backed prose.");

        Assert.Equal("# Resume\n\nEvidence-backed prose.", result.Trim());
    }

    [Fact]
    public void RemoveThinking_DropsUnclosedReasoningRatherThanLeakingIt()
    {
        Assert.Equal("Answer", LlamaSharpRuntime.RemoveThinking("Answer<think>private scratchpad"));
    }

    [Fact]
    public async Task DownloadAsync_CommitsCompletedDownloadToConfiguredPath()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"lucidresume-llama-{Guid.NewGuid():N}");
        var modelPath = Path.Combine(testDirectory, "model.gguf");
        try
        {
            var content = new byte[] { 0x47, 0x47, 0x55, 0x46 };
            using var http = new HttpClient(new StubHandler(content));
            var manager = new LlamaSharpModelManager(
                http,
                Options.Create(new LlamaSharpOptions
                {
                    ModelPath = modelPath,
                    DownloadUrl = "https://example.invalid/model.gguf"
                }),
                NullLogger<LlamaSharpModelManager>.Instance);

            await manager.DownloadAsync();

            Assert.True(manager.IsModelPresent);
            Assert.Equal(content, await File.ReadAllBytesAsync(modelPath));
            Assert.False(File.Exists(modelPath + ".download"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    private sealed class StubHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
    }
}
