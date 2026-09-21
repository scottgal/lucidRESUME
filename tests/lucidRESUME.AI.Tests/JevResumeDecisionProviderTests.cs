using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class JevResumeDecisionProviderTests
{
    [Fact]
    public async Task SendsClosedChoiceAndRedactsContactDetails()
    {
        string? captured = null;
        var handler = new StubHandler(async request =>
        {
            captured = await request.Content!.ReadAsStringAsync();
            var response = Json("""
                {
                  "model":"jev-1.13.0",
                  "answers":{"decision":{"type":"choice","choice":"experience","confidence":0.91,
                    "probabilities":{"experience":0.88,"other":0.12}}},
                  "usage":{"input_tokens":42,"output_tokens":3}
                }
                """);
            response.Headers.Add("x-typesafe-request-id", "req-1");
            return response;
        });
        var sut = Create(handler);

        var result = await sut.DecideAsync(new ResumeDecisionRequest(
            "decision-1", "section:1", "fnv1a64:test",
            "Contact jane@example.com or +44 7700 900123. See https://example.com/me",
            "Which section?",
            new Dictionary<string, string> { ["experience"] = "Work", ["other"] = "No match" }));

        Assert.Equal("experience", result.SelectedCandidate);
        Assert.Equal(0.88, result.Probabilities["experience"]);
        Assert.Equal("req-1", result.RequestId);
        Assert.DoesNotContain("jane@example.com", captured);
        Assert.DoesNotContain("7700", captured);
        Assert.DoesNotContain("example.com", captured);

        using var document = JsonDocument.Parse(captured!);
        var question = document.RootElement.GetProperty("questions").GetProperty("decision");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.Equal("Work", question.GetProperty("criteria").GetProperty("experience").GetString());
    }

    [Fact]
    public async Task RejectsChoiceThatCallerDidNotOffer()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {
              "model":"jev-1.13.0",
              "answers":{"decision":{"type":"choice","choice":"invented","confidence":1,
                "probabilities":{"invented":1}}},
              "usage":{}
            }
            """)));
        var sut = Create(handler);
        var request = new ResumeDecisionRequest("d", "s", "h", "state", "choose",
            new Dictionary<string, string> { ["experience"] = "Work", ["other"] = "No match" });

        await Assert.ThrowsAsync<InvalidDataException>(() => sut.DecideAsync(request));
    }

    private static JevResumeDecisionProvider Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new JevOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            BaseUrl = "https://example.invalid"
        }));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            callback(request);
    }
}
