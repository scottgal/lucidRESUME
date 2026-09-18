using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class OpenAiLiveIntegrationTests
{
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
