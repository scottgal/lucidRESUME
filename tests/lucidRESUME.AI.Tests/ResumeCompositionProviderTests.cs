using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Compiler;
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
