using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.AI;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Services;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.App.Tests;

public sealed class ProfileAiSettingsTests
{
    [Fact]
    public async Task Saving_model_choices_preserves_configured_servers_and_extraction_policy()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucid-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ai-settings.json");
            await File.WriteAllTextAsync(path, """
                {"Tailoring":{"Provider":"ollama","ExtractionProvider":"none","ComposeProse":true},
                 "Ollama":{"Model":"qwen3.8:latest","BaseUrl":"http://192.168.0.15:11434","NumCtx":16384},
                 "Nimble":{"Enabled":true,"BaseUrl":"http://192.168.0.15:11434","Model":"nimble"}}
                """);
            using var store = new JsonAppStore(Path.Combine(root, "data.json"));
            var http = new HttpClient(new Models());
            var manager = new LlamaSharpModelManager(http, Options.Create(new LlamaSharpOptions()), NullLogger<LlamaSharpModelManager>.Instance);
            var discovery = new ModelDiscoveryService(http, Options.Create(new OllamaOptions()), Options.Create(new AnthropicOptions()),
                Options.Create(new OpenAiOptions()), Options.Create(new LlamaSharpOptions()), NullLogger<ModelDiscoveryService>.Instance);
            var vm = new ProfilePageViewModel(store, discovery, new(path), new NoSecrets(), null!, null!, manager);
            var until = DateTime.UtcNow.AddSeconds(5);
            while ((vm.SelectedModel != "qwen3.8:latest" || vm.IsLoadingModels) && DateTime.UtcNow < until) await Task.Delay(10);
            Assert.Equal("qwen3.8:latest", vm.SelectedModel);
            await vm.SaveAiSettingsCommand.ExecuteAsync(null);
            Assert.StartsWith("Saved.", vm.AiSettingsStatus);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal("http://192.168.0.15:11434", saved.RootElement.GetProperty("Ollama").GetProperty("BaseUrl").GetString());
            Assert.Equal(16384, saved.RootElement.GetProperty("Ollama").GetProperty("NumCtx").GetInt32());
            Assert.Equal("none", saved.RootElement.GetProperty("Tailoring").GetProperty("ExtractionProvider").GetString());
            Assert.Equal("qwen3.8:latest", saved.RootElement.GetProperty("Ollama").GetProperty("Model").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Models : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"models\":[{\"name\":\"qwen3.8:latest\",\"capabilities\":[\"completion\"]}]}", Encoding.UTF8, "application/json") });
    }
    private sealed class NoSecrets : ISecretStore
    {
        public bool IsAvailable => false;
        public string BackendName => "test";
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
