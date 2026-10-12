using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.GitHub.Models;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.GitHub.Tests;

public sealed class GitHubCareerEvidenceTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public async Task Readme_skills_reach_the_career_with_source_passages_and_observed_contribution(long commits, bool eligible)
    {
        var options = Options.Create(new GitHubImportOptions { ReadmeAnalysisMaxRepositories = 0,
            DeepAnalysisMaxRepositories = 0 });
        using var http = new HttpClient(new Handler(commits)) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubApiClient(http, options, NullLogger<GitHubApiClient>.Instance);
        var importer = new GitHubSkillImporter(client, options, null!, NullLogger<GitHubSkillImporter>.Instance);
        var result = await importer.ImportAsync("alex");
        var profile = Assert.Single(result.ProjectProfiles);
        Assert.Equal(eligible, profile.EligibleForPersonalEvidence);
        Assert.Equal(commits, profile.Contribution.CandidateCommits);
        Assert.Equal(eligible, profile.Contribution.IsMainContributor);
        Assert.True(profile.AgeYears > 1);
        var project = Assert.Single(result.Projects);
        Assert.Contains("Docker", project.Technologies, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(project.EvidencePassages, passage => passage.Text.Contains("Docker") &&
            passage.SourceUri == "https://github.com/alex/product/blob/main/docs/Readme.md");
        var state = new AppState { Resumes = [new ResumeDocument { ResumeId = Guid.NewGuid(), Projects = [project] }] };
        var career = state.GetCompleteTranscript()!;
        var ledger = await new SkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(career);
        if (eligible)
        {
            Assert.Null(ledger.Find("Docker")); // README nominations require author review.
            foreach (var claim in career.EvidenceLedger.Claims.Where(c => c.Concepts.Contains("Docker", StringComparer.OrdinalIgnoreCase)))
                claim.Review = "accepted";
            ledger = await new SkillLedgerBuilder(new EmptyEmbedder()).BuildAsync(career);
            var skill = Assert.IsType<lucidRESUME.Core.Models.Skills.SkillLedgerEntry>(ledger.Find("Docker"));
            Assert.Equal(0, skill.CalculatedYears);
            Assert.Contains(career.EvidenceLedger.Evidence, evidence => evidence.ExternalUri?.EndsWith("docs/Readme.md") == true);
            Assert.All(result.SkillEntries, entry => Assert.Equal(0, entry.CalculatedYears));
        }
        else Assert.Null(ledger.Find("Docker"));
    }

    [Fact]
    public void Partial_contributor_listing_never_establishes_main_contributor_and_bots_are_excluded()
    {
        var result = RepositoryContribution.Assess("alex", [
            new GitHubContributor { Login = "alex", Contributions = 10 },
            new GitHubContributor { Login = "build[bot]", Type = "Bot", Contributions = 1000 },
            new GitHubContributor { Contributions = 5 }], complete: false);
        Assert.Null(result.IsMainContributor);
        Assert.Equal(1, result.CandidateRank);
        Assert.Equal(15, result.ObservedCommits);
    }

    private sealed class Handler(long commits) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            object value = path switch
            {
                "/users/alex" => new { login = "alex", name = "Alex" },
                var p when p.StartsWith("/users/alex/repos") => new[] { new { name = "product", html_url = "https://github.com/alex/product",
                    description = "A workflow product", size = 500, created_at = "2023-01-01T00:00:00Z", pushed_at = "2026-10-01T00:00:00Z" } },
                var p when p.Contains("/contributors?") => new[] { new { login = "alex", contributions = commits, type = "User" },
                    new { login = "other", contributions = 10L, type = "User" } },
                var p when p.EndsWith("/languages") => new Dictionary<string, long> { ["Python"] = 1000 },
                var p when p.EndsWith("/readme") => new { content = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    "# Workflow product\n\nThe product uses Docker containers with PostgreSQL for persistent workflow state. " +
                    "It provides a Python API for processing queued work and recovering from interrupted operations.")),
                    html_url = "https://github.com/alex/product/blob/main/docs/Readme.md", sha = "readme-blob" },
                _ => throw new InvalidOperationException(path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public float CosineSimilarity(float[] a, float[] b) => 0;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
    }
}
