using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobSpec.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.JobSpec.Tests;

public sealed class JobSkillClassifierTests
{
    [Fact]
    public async Task Classifies_ner_candidates_in_native_batches_and_caches_the_source_decisions()
    {
        var provider = new Decisions();
        var classifier = new JobSkillClassifier(provider);
        JdFieldCandidate[] candidates = [new("skill", "LLMs", .9, "ner:test"), new("skill", "System design", .9, "ner:test"),
            new("skill", "Remote", .9, "ner:test"), new("skill", "Business", .9, "ner:test")];
        const string text = "LLMs, system design, remote business.";
        var first = await classifier.ClassifyAsync(text, candidates, default);
        var second = await classifier.ClassifyAsync(text, candidates, default);
        Assert.Equal(1, provider.BatchCalls);
        Assert.Equal(new[] { "LLMs", "System design" }, first.Candidates.Select(c => c.Value));
        Assert.Equal("capability", first.Classes["System design"]);
        Assert.Equal("logistics", first.Classes["Remote"]);
        Assert.Equal(2, first.Decisions.Count(decision => decision.Model == "nimble-test"));
        Assert.Equal(2, first.Decisions.Count(decision => decision.Method == "conservative"));
        Assert.Same(first, second);
        Assert.Empty(first.Warnings);
    }

    [Fact]
    public async Task Low_probability_and_unavailable_decisions_cannot_promote_unknown_tokens()
    {
        var classifier = new JobSkillClassifier(new Decisions(probability: .55));
        JdFieldCandidate[] candidates = [new("skill", "LLMs", .9, "ner:test"), new("skill", "C#", .95, "structural"), new("skill", "Business", .9, "taxonomy")];
        var result = await classifier.ClassifyAsync("LLMs, C#, Business", candidates, default);
        Assert.Equal("C#", Assert.Single(result.Candidates).Value);
        var failed = await new JobSkillClassifier(new Decisions(fail: true)).ClassifyAsync("LLMs, C#, Business", candidates, default);
        Assert.Equal("C#", Assert.Single(failed.Candidates).Value);
        Assert.Single(failed.Warnings);
    }

    [Fact]
    public async Task Refreshes_saved_job_once_preserving_identity_and_manual_metadata()
    {
        var parser = new JobSpecParser(NullLogger<JobSpecParser>.Instance);
        var job = new JobDescription { Title = "My title", Company = "My company", RawText = "Developer at Extracted company\nRequired skills\n- C#\n", RequiredSkills = ["Business", "Remote"] };
        var id = job.JobId;
        Assert.True(await parser.EnsureRequirementsCurrentAsync(job));
        Assert.False(await parser.EnsureRequirementsCurrentAsync(job));
        Assert.Equal(id, job.JobId);
        Assert.Equal("My title", job.Title);
        Assert.Equal("My company", job.Company);
        Assert.Contains("C#", job.RequiredSkills);
        Assert.DoesNotContain("Business", job.RequiredSkills);
        job.RawText += "- SQL\n";
        Assert.True(await parser.EnsureRequirementsCurrentAsync(job));
        Assert.Contains("SQL", job.RequiredSkills);
    }

    [Fact]
    public async Task Mbn_sentence_advert_retains_intact_requirements_and_excludes_logistics()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbn-applied-ai.txt"));
        var job = await new JobSpecParser(NullLogger<JobSpecParser>.Instance).ParseFromTextAsync(text);
        Assert.Equal(6, job.RequiredSkills.Count);
        Assert.Equal(6, job.Responsibilities.Count);
        Assert.Equal(12, job.Competencies.Count);
        Assert.Contains(job.Competencies, c => c.Description.Contains("evaluate AI systems beyond whether a demo"));
        Assert.DoesNotContain(job.RequiredSkills, s => s.Contains("salary", StringComparison.OrdinalIgnoreCase) || s.Contains("visa", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(text, job.RawText);
    }

    [Fact]
    public async Task Configured_nimble_replaces_full_llm_job_extraction()
    {
        var llm = new CountingLlm();
        var services = new ServiceCollection().AddLogging().AddJobSpec();
        services.AddSingleton<IResumeDecisionProvider>(new Decisions());
        services.AddSingleton<ILlmExtractionService>(llm);
        await using var sp = services.BuildServiceProvider();
        var job = await sp.GetRequiredService<IJobSpecParser>().ParseFromTextAsync("Developer\nRequired skills\n- LLMs");
        Assert.Equal(0, llm.Calls);
        Assert.Contains("LLMs", job.RequiredSkills);
        Assert.EndsWith(":nimble", job.SkillAnalysisVersion);
    }

    private sealed class CountingLlm : ILlmExtractionService
    {
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public Task<string?> ExtractSkillsAsync(string text, CancellationToken ct = default) { Calls++; return Task.FromResult<string?>(null); }
        public Task<string?> ExtractNameAsync(string text, CancellationToken ct = default) => ExtractSkillsAsync(text, ct);
        public Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default) => ExtractSkillsAsync(text, ct);
    }

    private sealed class Decisions(double probability = 1, bool fail = false) : IResumeDecisionProvider
    {
        public string ProviderName => "test";
        public int BatchCalls { get; private set; }
        public Task<ResumeDecisionResult> DecideAsync(ResumeDecisionRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ResumeDecisionResult>> DecideBatchAsync(IReadOnlyList<ResumeDecisionRequest> requests, CancellationToken ct = default)
        {
            BatchCalls++;
            if (fail) throw new HttpRequestException("No decision endpoint");
            return Task.FromResult<IReadOnlyList<ResumeDecisionResult>>(requests.Select(request =>
            {
                var label = request.DecisionId["job-skill:".Length..];
                var selected = label switch { "LLMs" => "skill", "System design" => "capability", "Remote" => "logistics", _ => "noise" };
                var probabilities = request.Candidates.Keys.ToDictionary(key => key, key => key == selected ? probability : (1-probability)/3);
                return new ResumeDecisionResult(selected, probabilities, .99, ProviderName, "nimble-test");
            }).ToList());
        }
    }
}
