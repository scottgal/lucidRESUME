using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class TargetedProjectionTests
{
    [Fact]
    public async Task Ai_target_retains_current_specialist_role_and_concrete_ai_account_in_experience()
    {
        var resume = ResumeDocument.Create("source.md", "text/markdown", 0);
        resume.Personal.Summary = "Engineering leader building reliable software and developing teams.";
        var consultant = new WorkExperience
        {
            Title = "AI Development Consultant",
            Company = "Current client",
            IsCurrent = true,
            StartDate = new DateOnly(2026, 1, 1),
            Achievements = ["Consulting on systems using Code LLMs."]
        };
        var practice = new WorkExperience
        {
            Title = "Technical Consultant",
            Company = "Practice",
            IsCurrent = true,
            StartDate = new DateOnly(2012, 1, 1),
            Achievements = [
                "Managed distributed development teams.",
                "Reduced deployment times through cloud migrations.",
                "Modernised enterprise software architecture.",
                "Designed and deployed Azure OpenAI recommendation engines using vector embeddings."]
        };
        resume.Experience.AddRange([consultant, practice]);
        for (var i = 0; i < 7; i++) resume.Experience.Add(new WorkExperience
        {
            Title = "Engineer",
            Company = "Other " + i,
            StartDate = new DateOnly(2025 - i, 1, 1),
            Achievements = ["Built production software."]
        });
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var job = new JobDescription { Title = "Head of Applied AI Engineering", RawText = "AI engineering", RequiredSkills = ["AI"] };
        var output = await compressor.CompressAsync(resume, job);
        Assert.Contains(output.Projection.Experience, role => role.Id == consultant.Id && !role.IsCompact);
        var selectedPractice = Assert.Single(output.Projection.Experience, role => role.Id == practice.Id && !role.IsCompact);
        Assert.Contains(selectedPractice.Achievements, text => text.Contains("Azure OpenAI"));
        Assert.Equal(resume.Personal.Summary, output.Projection.Personal.Summary);
        Assert.Equal(4, practice.Achievements.Count);
        Assert.Equal(1, output.Projection.Projection!.Blocks.Count(block => block.ProseRef == "#summary:p1"));
    }

    [Fact]
    public async Task Authored_publications_survive_aggregation_and_projection_without_granting_role_skills()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Personal.FullName = "Example Author";
        var authored = ResumeDocument.Create("story.md", "text/markdown", 0);
        authored.Publications.Add(new Project
        {
            Name = "Agent engineering articles",
            Description = "AI architecture and agent memory.",
            Url = "https://example.com/ai"
        });
        var state = new lucidRESUME.Core.Persistence.AppState { Resumes = [source, authored], SelectedResumeId = source.ResumeId };
        var aggregate = state.BuildAggregateResume()!;
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var skillLedger = await new SkillLedgerBuilder(embedder, taxonomy).BuildAsync(aggregate);
        Assert.Null(skillLedger.Find("agent memory"));
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var output = await compressor.CompressAsync(aggregate, new JobDescription { Title = "AI Engineer", RequiredSkills = ["AI"] });
        Assert.Single(output.Projection.Publications);
        Assert.Contains("[Agent engineering articles](https://example.com/ai)", output.Markdown);
        var binding = Assert.Single(output.Projection.Projection!.Blocks, item => item.ProseRef == "#publications:p1");
        var claim = aggregate.EvidenceLedger.Claims.Single(item => item.Id == binding.ClaimId);
        Assert.Empty(claim.Concepts);
        Assert.Equal("https://example.com/ai", aggregate.EvidenceLedger.Evidence.Single(item => claim.EvidenceIds.Contains(item.Id)).ExternalUri);
    }

    [Fact]
    public void Detailed_selection_avoids_duplicate_employment_and_fills_the_slot_with_distinct_work()
    {
        var first = new WorkExperience
        {
            Title = "Senior Backend Developer",
            Company = "Very Jane Ltd",
            StartDate = new DateOnly(2013, 8, 1),
            EndDate = new DateOnly(2015, 6, 1),
            Achievements = ["Built payment services."]
        };
        var duplicate = new WorkExperience
        {
            Title = "Contract Senior Developer",
            Company = "Jane.com",
            StartDate = first.StartDate,
            EndDate = first.EndDate,
            Achievements = ["Built payment services for the same client."]
        };
        var distinct = new WorkExperience
        {
            Title = "Engineer",
            Company = "Another client",
            StartDate = new DateOnly(2016, 1, 1),
            Achievements = ["Built reliable APIs."]
        };
        var selected = SemanticCompressor.SelectDetailedRoleIds([first, duplicate, distinct],
            new Dictionary<Guid, double> { [first.Id] = 10, [duplicate.Id] = 9, [distinct.Id] = 8 }, maximumRelevantRoles: 2);
        Assert.Equal(2, selected.Count);
        Assert.Contains(first.Id, selected);
        Assert.Contains(distinct.Id, selected);
        Assert.DoesNotContain(duplicate.Id, selected);
    }

    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
