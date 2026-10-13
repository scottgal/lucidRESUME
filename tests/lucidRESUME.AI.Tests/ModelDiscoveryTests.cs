using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class ModelDiscoveryTests
{
    [Fact]
    public async Task Writer_picker_excludes_decision_and_embedding_models()
    {
        var service = new ModelDiscoveryService(new HttpClient(new Tags()), Options.Create(new OllamaOptions()),
            Options.Create(new AnthropicOptions()), Options.Create(new OpenAiOptions()), Options.Create(new LlamaSharpOptions()),
            NullLogger<ModelDiscoveryService>.Instance);
        var models = await service.ListOllamaModelsAsync();
        Assert.Equal(new[] { "qwen3.8:latest", "legacy-writer:latest" }, models.Select(model => model.Id));
    }

    private sealed class Tags : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"models":[
                    {"name":"nimble:latest","capabilities":["completion","decision"]},
                    {"name":"another-decision-model","capabilities":["decision"]},
                    {"name":"embedding-only","capabilities":["embedding"]},
                    {"name":"qwen3.8:latest","capabilities":["completion","tools"]},
                    {"name":"legacy-writer:latest"}]}
                """, Encoding.UTF8, "application/json") });
    }
}
