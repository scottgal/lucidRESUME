using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class NimbleResumeDecisionProviderTests
{
    [Fact]
    public async Task SendsClosedChoiceAndRedactsContactDetails()
    {
        string? captured = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("http://127.0.0.1:11435/v1/systemone", request.RequestUri?.ToString());
            Assert.Null(request.Headers.Authorization);
            captured = await request.Content!.ReadAsStringAsync();
            var response = Json("""
                {
                  "model":"nimble",
                  "answers":{"decision":{"type":"choice","choice":"experience","confidence":0.91,
                    "probabilities":{"experience":0.88,"other":0.12}}},
                  "usage":{"input_tokens":42,"output_tokens":3}
                }
                """);
            response.Headers.Add("x-request-id", "req-1");
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
              "model":"nimble",
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

    [Fact]
    public async Task Sends_batch_as_one_request_to_configured_lan_server_and_validates_each_answer()
    {
        var calls = 0;
        var handler = new StubHandler(async request =>
        {
            calls++;
            Assert.Equal("http://192.168.0.15:11434/v1/systemone", request.RequestUri!.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var questions = body.RootElement.GetProperty("questions");
            Assert.Equal(2, questions.EnumerateObject().Count());
            Assert.Contains("LLMs", questions.GetProperty("decision0").GetProperty("instructions").GetString());
            return Json("""
                {"model":"nimble","answers":{
                  "decision0":{"type":"choice","choice":"skill","confidence":0.9,"probabilities":{"skill":0.98,"noise":0.02}},
                  "decision1":{"type":"choice","choice":"noise","confidence":0.9,"probabilities":{"skill":0.02,"noise":0.98}}},"usage":{}}
                """);
        });
        var sut = new NimbleResumeDecisionProvider(new HttpClient(handler), Options.Create(new NimbleOptions
        { Enabled = true, BaseUrl = "http://192.168.0.15:11434" }));
        var choices = new Dictionary<string, string> { ["skill"] = "Skill", ["noise"] = "Noise" };
        var answers = await sut.DecideBatchAsync([
            new("1", "jd", "hash", "LLMs in a remote role", "Classify LLMs", choices),
            new("2", "jd", "hash", "LLMs in a remote role", "Classify Remote", choices)]);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "skill", "noise" }, answers.Select(answer => answer.SelectedCandidate));
    }

    [Fact]
    public async Task Rejects_an_incomplete_batch_instead_of_misaligning_labels()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {"model":"nimble","answers":{"decision0":{"type":"choice","choice":"skill","confidence":1,"probabilities":{"skill":1,"noise":0}}},"usage":{}}
            """)));
        var request = new ResumeDecisionRequest("1", "jd", "hash", "state", "Classify", new Dictionary<string, string> { ["skill"] = "Skill", ["noise"] = "Noise" });
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(handler).DecideBatchAsync([request, request]));
    }

    private static NimbleResumeDecisionProvider Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new NimbleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:11435"
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
