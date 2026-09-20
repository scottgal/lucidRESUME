using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace lucidRESUME.Web.Tests;

public sealed class WebControlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WebControlTests(WebApplicationFactory<Program> factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    [Fact]
    public async Task Page_explains_complete_master_to_projection_flow()
    {
        var html = await _client.GetStringAsync("/lucidresume/");
        Assert.Contains("Complete résumé", html);
        Assert.Contains("complete human-written résumé and JobML ledger", html);
        Assert.Contains("Paste the job. Get the right version of you.", html);
    }

    [Fact]
    public async Task Mutation_without_antiforgery_token_is_rejected()
    {
        var response = await _client.PostAsync("/lucidresume/api/ledger",
            new StringContent("not a ledger", Encoding.UTF8, "text/markdown"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
