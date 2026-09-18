using System.Net;
using lucidRESUME.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class LlamaSharpIntegrationTests
{
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
