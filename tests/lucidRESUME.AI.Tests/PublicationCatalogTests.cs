using System.Net;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class PublicationCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lucid-publications-" + Guid.NewGuid().ToString("N"));
    public PublicationCatalogTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task Import_is_incremental_and_content_changes_reset_publication_approval()
    {
        var path = Path.Combine(_directory, "agent-memory.md");
        const string prose = "This article explains agent memory retrieval and the trade-offs involved in retaining workflow context.";
        await File.WriteAllTextAsync(path, "# Agent memory\n\n<datetime>2026-10-01</datetime>\n\n<!-- category -- AI, Agents -->\n\n" + prose);
        var first = await PublicationCatalog.ImportAsync(_directory, [], new(2026, 10, 12));
        var article = Assert.Single(first.Articles);
        Assert.Equal("https://www.mostlylucid.net/blog/agent-memory", article.Url);
        Assert.Contains(article.EvidencePassages, passage => passage.Text == prose);
        Assert.Empty(article.Technologies);
        article.EvidenceMetadata["review"] = "accepted";
        article.EvidenceMetadata["live_url_verified"] = "true";
        var same = await PublicationCatalog.ImportAsync(_directory, first.Articles, new(2026, 10, 12));
        Assert.Equal(1, same.Reused);
        Assert.Same(article, same.Articles[0]);
        await File.AppendAllTextAsync(path, "\n\nThe revised article also compares experimental approaches to evaluation and regression testing.");
        var changed = Assert.Single((await PublicationCatalog.ImportAsync(_directory, same.Articles, new(2026, 10, 12))).Articles);
        Assert.Equal(article.Id, changed.Id);
        Assert.Equal("required", changed.EvidenceMetadata["review"]);
        Assert.Equal("false", changed.EvidenceMetadata["live_url_verified"]);
        Assert.NotEqual(article.EvidenceMetadata["content_hash"], changed.EvidenceMetadata["content_hash"]);
    }

    [Fact]
    public async Task Future_articles_and_translations_are_excluded()
    {
        foreach (var (name, date) in new[] { ("future", "2027-01-01"), ("translated.fr", "2026-01-01") })
            await File.WriteAllTextAsync(Path.Combine(_directory, name + ".md"), $"# Article\n\n<datetime>{date}</datetime>\n\nArticle text.");
        Assert.Empty((await PublicationCatalog.ImportAsync(_directory, [], new(2026, 10, 12))).Articles);
    }

    [Fact]
    public void Only_reviewed_live_publications_are_selected_and_they_never_grant_skills()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        var article = new Project { Name = "Agent memory", Description = "Agent memory retrieval and workflow context.", Url = "https://www.mostlylucid.net/blog/agent-memory",
            EvidenceMetadata = new() { ["provider"] = "blog", ["review"] = "accepted", ["live_url_verified"] = "false" } };
        source.Publications.Add(article);
        var job = new JobDescription { RequiredSkills = ["agent memory retrieval"] };
        Assert.Empty(RelatedWorkSelector.Select(source, job, [], 3));
        article.EvidenceMetadata["live_url_verified"] = "true";
        var link = Assert.Single(RelatedWorkSelector.Select(source, job, [], 3));
        Assert.Equal("publication", link.Kind);
        Assert.NotEmpty(link.ClaimIds);
        Assert.All(source.EvidenceLedger.Claims, claim => Assert.Empty(claim.Concepts));
        source.EvidenceLedger.Claims.Single().Review = "rejected";
        source.Personal.Summary = "An unrelated career edit must not reset a publication rejection.";
        Assert.Empty(RelatedWorkSelector.Select(source, job, [], 3));
        Assert.Equal("rejected", source.EvidenceLedger.Claims.Single(claim => claim.Kind == "publication").Review);
        article.EvidenceMetadata["review"] = "required";
        Assert.Empty(RelatedWorkSelector.Select(source, job, [], 3));
    }

    [Fact]
    public async Task Discovery_uses_public_topics_and_tolerates_offline_or_non_json_responses()
    {
        var handler = new StubHandler();
        var service = new PublicationDiscoveryService(new HttpClient(handler));
        Assert.Equal(new[] { "agent-memory" }, await service.SuggestAsync(["agent memory"]));
        Assert.Equal("www.mostlylucid.net", handler.LastUri!.Host);
        Assert.Contains("agent%20memory", handler.LastUri.OriginalString);
        handler.Body = "<html>Unavailable</html>";
        Assert.Empty(await service.SuggestAsync(["agent memory"]));
        handler.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Empty(await service.SuggestAsync(["agent memory"]));
        Assert.False(await service.VerifyAsync("https://example.com/blog/article"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body = "[{\"slug\":\"agent-memory\",\"score\":0.8}]";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Uri? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body), RequestMessage = request });
        }
    }
}
