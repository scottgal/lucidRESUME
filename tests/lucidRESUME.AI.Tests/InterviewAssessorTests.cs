using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class InterviewAssessorTests
{
    private static InterviewAssessmentRequest Request(string? action = null) => new(Guid.NewGuid(), "Terraform", "Platform Engineer",
        "CloudFormation automation", "Multiple teams needed consistent deployments.",
        action ?? "I wrote deployment checks and documented their limits.", "The teams could detect configuration errors before release.");

    [Theory]
    [InlineData("ollama")]
    [InlineData("cloudflare")]
    public async Task Model_selects_a_caller_owned_question_and_returns_audit_metadata(string provider)
    {
        var handler = new Handler(async message =>
        {
            Assert.Contains(provider == "ollama" ? "/v1/systemone" : "/ai/run/@cf/cloudflare/clef-flash", message.RequestUri!.ToString());
            Assert.Equal(provider == "cloudflare" ? "test-secret" : null, message.Headers.Authorization?.Parameter);
            using var request = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            var questions = request.RootElement.GetProperty("questions");
            Assert.Equal(3, questions.EnumerateObject().Count());
            Assert.Equal(8, questions.GetProperty("next_probe").GetProperty("criteria").EnumerateObject().Count());
            return Json(Response(provider, "decision"));
        });
        var request = Request();
        var result = await Create(handler, provider).AssessAsync(request);
        Assert.True(result.UsedModel);
        Assert.Equal(InterviewProbe.Decision, result.Probe);
        Assert.Contains("trade-off", result.Question);
        Assert.Equal(provider, result.Provider);
        Assert.Equal(lucidRESUME.Matching.InterviewAssessmentRules.Hash(request), result.InputHash);
    }

    [Theory]
    [InlineData("{\"model\":\"nimble\",\"answers\":null}")]
    [InlineData("{\"model\":\"nimble\",\"answers\":{\"next_probe\":null}}")]
    [InlineData("{\"model\":\"nimble\",\"answers\":{}}")]
    [InlineData("not json")]
    public async Task Malformed_responses_fall_back_without_modifying_answers(string response)
    {
        var request = Request();
        var result = await Create(new Handler(_ => Task.FromResult(Json(response)))).AssessAsync(request);
        Assert.False(result.UsedModel);
        Assert.Equal(InterviewProbe.Review, result.Probe);
        Assert.Contains("unavailable", result.Feedback);
        Assert.Equal("I wrote deployment checks and documented their limits.", request.Action);
    }

    [Fact]
    public async Task Weak_probability_does_not_override_local_follow_up()
    {
        var result = await Create(new Handler(_ => Task.FromResult(Json(Response("ollama", "decision", .3))))).AssessAsync(Request());
        Assert.False(result.UsedModel);
        Assert.Equal(InterviewProbe.Review, result.Probe);
        Assert.Contains("uncertain", result.Feedback);
    }

    [Theory]
    [InlineData("We implemented all the changes as a team.", InterviewProbe.Contribution)]
    [InlineData("I have never used Terraform in a role.", InterviewProbe.Skip)]
    public async Task Model_review_cannot_override_missing_personal_action_or_declared_gap(string action, InterviewProbe expected)
    {
        var result = await Create(new Handler(_ => Task.FromResult(Json(Response("ollama", "review"))))).AssessAsync(Request(action));
        Assert.Equal(expected, result.Probe);
    }

    [Fact]
    public async Task Disabled_assessment_never_calls_a_provider()
    {
        var options = new InterviewOptions();
        var result = await new SystemOneInterviewAssessor(new HttpClient(new Handler(_ => throw new InvalidOperationException("Unexpected call"))),
            Options.Create(options)).AssessAsync(Request());
        Assert.False(result.UsedModel);
    }

    private static SystemOneInterviewAssessor Create(HttpMessageHandler handler, string provider = "ollama") => new(new HttpClient(handler),
        Options.Create(new InterviewOptions { Enabled = true, Provider = provider, Model = provider == "ollama" ? "nimble" : "clef-flash",
            CloudflareAccountId = "account123", ApiKey = "test-secret" }));

    private static string Response(string provider, string probe, double probability = .96)
    {
        object Answer(string choice, string[] choices, double p) => new
        { type = "choice", choice, confidence = p, probabilities = choices.ToDictionary(key => key, key => key == choice ? p : (1 - p) / (choices.Length - 1)) };
        object response = new { model = provider == "ollama" ? "nimble" : "clef-flash", answers = new Dictionary<string, object>
        {
            ["next_probe"] = Answer(probe, ["context", "contribution", "decision", "outcome", "verification", "relevance", "review", "skip"], probability),
            ["contribution"] = Answer("concrete", ["missing", "vague", "concrete"], .96),
            ["outcome"] = Answer("observed", ["missing", "intended", "observed"], .96)
        }};
        return JsonSerializer.Serialize(provider == "cloudflare" ? new { success = true, result = response } : response);
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => callback(request); }
}
