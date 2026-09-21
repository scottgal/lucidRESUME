using System.Net;
using System.Text;
using lucidRESUME.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class OpenAiExtractionServiceTests
{
    [Fact]
    public async Task ExtractJsonAsync_SendsCallerPromptWithoutSkillsPromptSubstitution()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"output":[{"type":"message","content":[{"type":"output_text","text":"{\"choice\":\"none\"}"}]}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var service = new OpenAiExtractionService(
            new HttpClient(handler),
            Options.Create(new OpenAiOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://example.test/v1",
                ExtractionModel = "test-model"
            }),
            NullLogger<OpenAiExtractionService>.Instance);

        var result = await service.ExtractJsonAsync("CLASSIFY_THIS_EXACT_PROMPT");

        Assert.Equal("{\"choice\":\"none\"}", result);
        Assert.Contains("CLASSIFY_THIS_EXACT_PROMPT", requestBody);
        Assert.DoesNotContain("List all technical skills", requestBody);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
