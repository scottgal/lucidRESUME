using System.Net;
using lucidRESUME.Ingestion.Web;

namespace lucidRESUME.Core.Tests;

public sealed class LinkedPostImporterTests
{
    [Fact]
    public async Task Importer_CapturesCanonicalBibliographyAndFullResolutionFingerprint()
    {
        const string html = """
            <!doctype html>
            <html>
            <head>
              <title>Fallback title</title>
              <link rel="canonical" href="https://mostlylucid.net/blog/reduced-rag">
              <meta property="og:title" content="Reduced RAG">
              <meta property="og:site_name" content="MostlyLucid">
              <meta name="author" content="Scott Galloway">
              <meta property="article:published_time" content="2025-04-12T09:00:00Z">
            </head>
            <body>
              <nav>Navigation noise</nav>
              <article><h1>Reduced RAG</h1><p>Sentence-level evidence remains attached to retrieval results.</p></article>
            </body>
            </html>
            """;
        using var http = new HttpClient(new StubHandler(html));

        var post = await new LinkedPostImporter(http)
            .ImportAsync(new Uri("https://mostlylucid.net/old-link"));
        var evidence = post.ToJobMlEvidence();

        Assert.Equal("https://mostlylucid.net/blog/reduced-rag", post.CanonicalUri.ToString().TrimEnd('/'));
        Assert.Equal("Reduced RAG", post.Title);
        Assert.Equal(["Scott Galloway"], post.Authors);
        Assert.Equal("MostlyLucid", post.Publisher);
        Assert.Equal(new DateOnly(2025, 4, 12), post.PublishedOn);
        Assert.StartsWith("fnv1a64:", post.ContentFingerprint);
        Assert.Contains("Sentence-level evidence", post.Content);
        Assert.DoesNotContain("Navigation noise", post.Content);
        Assert.Equal("article", evidence.Type);
        Assert.Equal(post.ContentFingerprint, evidence.Fingerprint!.Text);
        Assert.Equal("2025-04-12", evidence.Published);
    }

    [Fact]
    public async Task Importer_IgnoresNonHttpCanonicalUri()
    {
        const string html = """
            <html><head><title>Safe title</title>
            <link rel="canonical" href="javascript:alert('no')">
            </head><body><article>Safe content</article></body></html>
            """;
        using var http = new HttpClient(new StubHandler(html));

        var post = await new LinkedPostImporter(http)
            .ImportAsync(new Uri("https://example.com/safe"));

        Assert.Equal("https://example.com/safe", post.CanonicalUri.ToString());
    }

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
                RequestMessage = request
            });
    }
}
