using lucidRESUME.AI;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using lucidRESUME.JobML;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace lucidRESUME.AI.Tests;

public sealed class OpenAiLiveIntegrationTests
{
    [Fact]
    public async Task ResponsesApi_PerformsBoundedCompositionPass_WhenApiKeyIsSupplied()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        var provider = new OpenAiResumeCompositionProvider(new HttpClient(),
            Options.Create(new OpenAiOptions
            {
                ApiKey = apiKey,
                Model = Environment.GetEnvironmentVariable("LUCIDRESUME_OPENAI_TEST_MODEL") ?? "gpt-5-mini",
                MaxTokens = 1_200,
                TimeoutSeconds = 120
            }));
        var claim = new JobMlClaim
        {
            Id = "claim-1",
            Subject = "role-1",
            Statement = "Led a 10-person engineering team through platform change."
        };
        var selected = new SelectedClaim(claim, "Example Ltd",
            "Led a 10-person engineering team through platform change, while remaining hands-on with C#.",
            ["evidence-1"], .95, []);
        var packet = new EvidencePacket("role-1", "Example Ltd", "Tighten", 30, [selected], ["req-1"]);
        var requirement = new CompilerRequirement("req-1", "engineering leadership", RequirementKind.Required,
            "engineering leadership");
        var block = new CompositionBlock("role-1", selected.Prose, [claim.Id], ["evidence-1"]);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "onnx");

        var draft = await provider.RunPassAsync(new CompositionPassRequest(
            CompositionPass.Tighten, "untrusted vacancy text", manifest, [block], [block]));

        Assert.Empty(new CompositionValidator().Validate(draft, [block], manifest));
        var output = Assert.Single(draft.Blocks);
        Assert.Equal([claim.Id], output.ClaimIds);
        Assert.Equal(["evidence-1"], output.EvidenceIds);
        Assert.DoesNotContain('—', output.Text);
    }

    [Fact]
    public async Task ResponsesApi_GeneratesGroundedResume_WhenApiKeyIsSupplied()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        var service = new OpenAiTailoringService(
            new HttpClient(),
            Options.Create(new OpenAiOptions { ApiKey = apiKey, MaxTokens = 1_200, TimeoutSeconds = 120 }),
            Options.Create(new TailoringOptions()),
            NullLogger<OpenAiTailoringService>.Instance,
            new NoTerms(),
            new NoCoverage());
        var resume = ResumeDocument.Create("jane.md", "text/markdown", 0);
        resume.Personal.FullName = "Jane Smith";
        resume.RawMarkdown = "# Jane Smith\n\n## Experience\n\n### Engineer - Example Corp\n\n- Reduced API latency by 40%.";
        resume.Experience.Add(new WorkExperience
        {
            Company = "Example Corp",
            Title = "Engineer",
            Achievements = ["Reduced API latency by 40%."],
            ImportSources = ["integration-test"]
        });
        var job = JobDescription.Create("Seeking an engineer who improves API performance.",
            new JobSource { Type = JobSourceType.PastedText });
        job.Title = "API Engineer";
        job.Company = "Example Ltd";

        var result = await service.TailorAsync(resume, job, new UserProfile());

        Assert.Contains("Jane Smith", result.RawMarkdown);
        Assert.Contains("40%", result.RawMarkdown);
        Assert.DoesNotContain("MACHINE AREA", result.RawMarkdown);
    }

    [Fact]
    public async Task ResponsesApi_ColdParsesCompactJobMl_WhenApiKeyIsSupplied()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        var full = JobMlDraftGenerator.Generate(
            "# Jane Smith\n\n## Experience\n\nBuilt an evidence-linked retrieval platform.");
        full.Data.Document.CompleteLedger = "https://example.com/jane.jobml";
        var claim = Assert.Single(full.Data.Claims);
        claim.Review = "accepted";
        claim.Evidence.Add(new JobMlEvidence
        {
            Id = "reduced-rag",
            Type = "article",
            Uri = "https://mostlylucid.net/reduced-rag",
            Title = "Reduced RAG",
            Authors = ["S. Galloway"]
        });
        var compact = CJobMlProjector.Project(full).Markdown;

        using var http = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var request = new
        {
            model = Environment.GetEnvironmentVariable("LUCIDRESUME_OPENAI_TEST_MODEL") ?? "gpt-5-mini",
            max_output_tokens = 500,
            input = "Read this resume as an unfamiliar document format. Identify the prose claim carrying a numbered citation, the supporting reference number and URL, and the complete machine-readable record URL. Do not use outside information.\n\n" + compact,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "cold_resume_parse",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            claim = new { type = "string" },
                            reference_number = new { type = "integer" },
                            evidence_url = new { type = "string" },
                            complete_record_url = new { type = "string" }
                        },
                        required = new[] { "claim", "reference_number", "evidence_url", "complete_record_url" },
                        additionalProperties = false
                    }
                }
            }
        };
        using var response = await http.PostAsJsonAsync("responses", request);
        var json = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        var answer = OpenAiTailoringService.ExtractOutputText(json) ?? "{}";
        using var parsed = JsonDocument.Parse(answer);
        var root = parsed.RootElement;

        Assert.Contains("Built an evidence-linked retrieval platform",
            root.GetProperty("claim").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, root.GetProperty("reference_number").GetInt32());
        Assert.Equal("https://mostlylucid.net/reduced-rag", root.GetProperty("evidence_url").GetString());
        Assert.Equal("https://example.com/jane.jobml", root.GetProperty("complete_record_url").GetString());
    }

    private sealed class NoTerms : ITermNormalizer
    {
        public Task<IReadOnlyList<TermMatch>> FindMatchesAsync(IReadOnlyList<string> targetTerms,
            IReadOnlyList<string> sourceTerms, float minSimilarity = 0.82f, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TermMatch>>([]);
    }

    private sealed class NoCoverage : ICoverageAnalyser
    {
        public Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
            CancellationToken ct = default) => throw new InvalidOperationException("Coverage omitted in live API smoke test.");
    }
}
