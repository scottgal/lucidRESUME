using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Compiler;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class ResumeCompositionProviderTests
{
    [Fact]
    public async Task OpenAi_provider_is_stateless_structured_and_evidence_bounded()
    {
        var handler = new CaptureHandler();
        var provider = new OpenAiResumeCompositionProvider(new HttpClient(handler),
            Options.Create(new OpenAiOptions
            {
                ApiKey = "test-only",
                BaseUrl = "https://example.test/v1",
                Model = "test-model"
            }));
        var block = new CompositionBlock("role", "Led an engineering team.", ["claim-1"], ["evidence-1"]);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [], [], [], "lexical");

        var result = await provider.RunPassAsync(new CompositionPassRequest(
            CompositionPass.Tighten, "Target role", manifest, [block], [block]));

        Assert.Equal("Led an engineering team.", Assert.Single(result.Blocks).Text);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.True(request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        var input = request.RootElement.GetProperty("input").GetString();
        Assert.Contains("Led an engineering team.", input);
        Assert.DoesNotContain("invent", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_provider_recovers_only_complete_sections_from_truncated_json()
    {
        const string truncated = """
            model preamble
            {"sections":[
              {"sectionId":"summary","text":"Builder with a {practical} focus."},
              {"sectionId":"role","text":"Led delivery with an escaped \"quote\"."},
              {"sectionId":"unfinished","text":"This must not be accepted
            """;

        var result = LlamaSharpResumeCompositionProvider.ParseBatch(truncated);

        Assert.Equal(2, result.Sections.Count);
        Assert.Equal("summary", result.Sections[0].SectionId);
        Assert.Equal("Led delivery with an escaped \"quote\".", result.Sections[1].Text);
        Assert.Contains(result.Warnings, warning => warning.Contains("truncated"));
    }

    [Fact]
    public async Task Ollama_editor_uses_local_structured_output_and_preserves_evidence_ids()
    {
        var handler = new OllamaCaptureHandler();
        var provider = new OllamaResumeCompositionProvider(new HttpClient(handler),
            Options.Create(new OllamaOptions
            {
                CompositionEnabled = true,
                BaseUrl = "http://127.0.0.1:11435",
                Model = "qwen3.5:latest"
            }));
        var claim = new JobMlClaim { Id = "claim-1", Subject = "role", Type = "achievement" };
        var selected = new SelectedClaim(claim, "Example", "Built an AI service.", ["evidence-1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "Tighten", 20, [selected], [], "experience");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [], [packet], [], [], "lexical");
        var block = new CompositionBlock("role", "Built an AI service.", ["claim-1"], ["evidence-1"]);

        var result = await provider.RunPassAsync(new CompositionPassRequest(
            CompositionPass.Tighten, "Head of AI", manifest, [block], [block]));

        Assert.True(provider.IsAvailable);
        Assert.Equal("Built an AI service.", Assert.Single(result.Blocks).Text);
        Assert.Equal(["claim-1"], result.Blocks[0].ClaimIds);
        Assert.Equal(["evidence-1"], result.Blocks[0].EvidenceIds);
        Assert.Equal("http://127.0.0.1:11435/api/chat", handler.RequestUri?.ToString());
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal("object", request.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("qwen3.5:latest", request.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Ollama_editor_edits_summary_without_job_ad_facts_in_prompt()
    {
        var handler = new OllamaCaptureHandler("summary");
        var provider = new OllamaResumeCompositionProvider(new HttpClient(handler),
            Options.Create(new OllamaOptions
            {
                CompositionEnabled = true,
                BaseUrl = "http://127.0.0.1:11435",
                Model = "qwen3.5:latest"
            }));
        var claim = new JobMlClaim { Id = "claim-1", Subject = "person", Type = "summary" };
        var selected = new SelectedClaim(claim, "Person", "I build services.", ["evidence-1"], .9, []);
        var packet = new EvidencePacket("summary", "Professional Summary", "Tighten", 80,
            [selected], [], "summary");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [], [packet], [], [], "lexical");
        var block = new CompositionBlock("summary", "I build services.", ["claim-1"], ["evidence-1"]);

        await provider.RunPassAsync(new CompositionPassRequest(CompositionPass.Tighten,
            "Build vector search with Azure", manifest, [block], [block]));

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var userPrompt = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("\"target_role\":\"\"", userPrompt);
        Assert.Contains("\"maximum_words\":65", userPrompt);
        Assert.DoesNotContain("vector search", userPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure", userPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OllamaCaptureHandler(string sectionId = "role") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var content = JsonSerializer.Serialize(new
            {
                sections = new[] { new { sectionId, text = "Built an AI service." } }
            });
            var response = JsonSerializer.Serialize(new { message = new { content } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var output = "{\"blocks\":[{\"sectionId\":\"role\",\"text\":\"Led an engineering team.\",\"claimIds\":[\"claim-1\"],\"evidenceIds\":[\"evidence-1\"]}],\"warnings\":[]}";
            var response = JsonSerializer.Serialize(new { output_text = output });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
