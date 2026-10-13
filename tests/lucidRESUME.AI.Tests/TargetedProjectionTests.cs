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
    public async Task Concise_projection_still_evidences_required_tenure_and_warns_without_blocking_when_short()
    {
        var resume = ResumeDocument.Create("career.md", "text/markdown", 0);
        resume.Experience = Enumerable.Range(2010, 12).Select(year => new WorkExperience
        {
            Company = $"Employer {year}",
            Title = "Software Engineer",
            StartDate = new(year, 1, 1),
            EndDate = new(year + 1, 1, 1),
            Achievements = ["Built reliable APIs with C#."]
        }).ToList();
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var search = new CountingSearch();
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance, proseSearch: search);
        var job = new JobDescription { Title = "Software Engineer", RequiredSkills = ["C#"], RequiredYearsExperience = 10 };
        var result = await compressor.CompressAsync(resume, job, generationOptions: new() { Length = ResumeLength.Concise });
        Assert.Equal(1, search.Queries); // One requirement query for the complete career, rather than twelve.
        Assert.Equal(3, result.Projection.Experience.Count(role => !role.IsCompact));
        Assert.True(result.Projection.Experience.Count(role => role.IsCompact) >= 7);
        Assert.True(ExperienceDetailPlan.CoveredYears(result.Projection.Experience, DateOnly.FromDateTime(DateTime.UtcNow)) >= 9.99);
        Assert.DoesNotContain(result.Projection.GenerationWarnings, warning => warning.Contains("non-overlapping years"));
        job.RequiredYearsExperience = 20;
        var shortfall = await compressor.CompressAsync(resume, job, generationOptions: new() { Length = ResumeLength.Concise });
        Assert.NotEmpty(shortfall.Projection.Experience);
        Assert.Contains(shortfall.Projection.GenerationWarnings, warning => warning.Contains("20 years") && warning.Contains("non-overlapping"));
        Assert.All(shortfall.Projection.Projection!.Blocks, block =>
            Assert.Contains(resume.EvidenceLedger!.Claims, claim => claim.Id == block.ClaimId && claim.Review == "accepted"));
    }

    [Fact]
    public async Task Review_only_rejections_remove_summary_projects_education_and_skills_from_the_projection()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Personal.Summary = "Worked on billing recovery.";
        source.Experience.Add(new() { Title = "Engineer", Company = "Example", Achievements = ["Built billing recovery workflows."] });
        source.Projects.Add(new() { Name = "Billing recovery", Description = "Billing recovery workflows in C#.", Technologies = ["C#"] });
        source.Skills.Add(new() { Name = "C#" });
        source.Education.Add(new() { Degree = "BSc", Institution = "Example University" });
        var ledger = lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(source);
        foreach (var claim in ledger.Claims.Where(claim => claim.Kind is "project" or "skill" or "education" || claim.Statement == source.Personal.Summary))
            claim.Review = "rejected";
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var result = await compressor.CompressAsync(source, new JobDescription { Title = "Engineer", RequiredSkills = ["C#"], Responsibilities = ["billing recovery"] });
        Assert.Null(result.Projection.Personal.Summary);
        Assert.Empty(result.Projection.Projects);
        Assert.Empty(result.Projection.Education);
        Assert.Empty(result.Projection.Skills);
        Assert.All(result.Projection.Projection!.Blocks, block => Assert.Equal("accepted", ledger.Claims.Single(claim => claim.Id == block.ClaimId).Review));
    }

    [Fact]
    public async Task Extended_projection_selects_more_supported_roles_and_retains_generation_preferences()
    {
        var resume = ResumeDocument.Create("career.md", "text/markdown", 0);
        resume.Experience = Enumerable.Range(0, 10).Select(i => new WorkExperience
        {
            Title = "Software Engineer",
            Company = $"Example {i}",
            StartDate = new DateOnly(2000 + i, 1, 1),
            EndDate = new DateOnly(2000 + i, 12, 31),
            Achievements = ["Built reliable APIs with C#."]
        }).ToList();
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var job = new JobDescription { Title = "Software Engineer", RawText = "Build reliable APIs with C#." };
        var concise = await compressor.CompressAsync(resume, job, generationOptions: new() { Length = ResumeLength.Concise });
        var preferences = new ResumeGenerationOptions { Length = ResumeLength.Extended, Detail = ResumeDetail.Thorough, Tone = ResumeTone.Expressive };
        var extended = await compressor.CompressAsync(resume, job, generationOptions: preferences);
        Assert.Equal(3, concise.Projection.Experience.Count(role => !role.IsCompact));
        Assert.Equal(10, extended.Projection.Experience.Count(role => !role.IsCompact));
        Assert.Equal(preferences, extended.Projection.GenerationOptions);
        Assert.Equal(1, extended.Projection.MinimumOutputPages);
        Assert.All(extended.Projection.Projection!.Blocks, block =>
            Assert.Contains(resume.EvidenceLedger!.Claims, claim => claim.Id == block.ClaimId));
    }

    [Fact]
    public async Task Different_jobs_select_different_achievements_without_competencies_or_embeddings()
    {
        var resume = ResumeDocument.Create("career.md", "text/markdown", 0);
        var role = new WorkExperience
        {
            Title = "Software Engineer",
            Company = "Example",
            IsCurrent = true,
            StartDate = new DateOnly(2020, 1, 1),
            Achievements = ["Built internal APIs in C#.", "Delivered observability using OpenTelemetry.",
                "Implemented cloud deployment pipelines.",
                "Optimised PostgreSQL queries and database backups with recovery drills.",
                "Improved accessible interfaces with keyboard navigation and screen reader testing."]
        };
        resume.Experience.Add(role);
        var oldResearch = new WorkExperience
        {
            Company = "Research Lab",
            Title = "Research Developer",
            StartDate = new(2010, 1, 1),
            EndDate = new(2015, 1, 1),
            Achievements = ["Built a Python classification prototype using PyTorch."]
        };
        resume.Experience.Add(oldResearch);
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var database = await compressor.CompressAsync(resume, new JobDescription
        {
            Title = "Platform Engineer",
            RawText = "Own PostgreSQL queries, database backups and recovery drills.",
            Responsibilities = ["Own PostgreSQL queries, database backups and recovery drills."]
        });
        var accessibility = await compressor.CompressAsync(resume, new JobDescription
        {
            Title = "Platform Engineer",
            RawText = "Improve accessible interfaces, keyboard navigation and screen reader testing.",
            Responsibilities = ["Improve accessible interfaces, keyboard navigation and screen reader testing."]
        });
        var databaseRole = Assert.Single(database.Projection.Experience, item => !item.IsCompact);
        var accessibilityRole = Assert.Single(accessibility.Projection.Experience, item => !item.IsCompact);
        Assert.Contains("PostgreSQL", databaseRole.Achievements[0]);
        Assert.Contains("accessible interfaces", accessibilityRole.Achievements[0]);
        Assert.DoesNotContain(databaseRole.Achievements, text => text.Contains("screen reader"));
        Assert.DoesNotContain(accessibilityRole.Achievements, text => text.Contains("PostgreSQL"));
        Assert.Equal(5, role.Achievements.Count);
        var compact = Assert.Single(database.Projection.Experience, item => item.IsCompact);
        Assert.Equal(oldResearch.Id, compact.Id);
        Assert.Empty(compact.Achievements);
        Assert.All(database.Projection.Projection!.Blocks, block => Assert.Contains(resume.EvidenceLedger!.Claims, claim => claim.Id == block.ClaimId));
    }

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

    [Fact]
    public async Task Synthesis_packet_keeps_all_duplicate_and_complementary_claim_bindings_and_rejects_reviewed_out_claims()
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Experience.Add(new()
        {
            Title = "Engineer",
            Company = "Example",
            IsCurrent = true,
            Achievements = ["Built billing retry queues in C#.", "Built billing retry queues in C#.",
                "Added billing retry audit trails.", "Owned billing retry architecture for every customer."]
        });
        var ledger = lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(source);
        var embedder = new EmptyEmbedder();
        await new CareerProseAnalyser(embedder).AnalyseAsync(source);
        ledger.Claims.Single(claim => claim.Statement.StartsWith("Owned billing")).Review = "rejected";
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var result = await compressor.CompressAsync(source, new JobDescription
        { Title = "Engineer", RequiredSkills = ["C#"], Responsibilities = ["billing retry queues and audit trails"] });
        var prose = Assert.Single(Assert.Single(result.Projection.Experience).Achievements);
        Assert.Contains("C#", prose);
        Assert.Contains("audit trails", prose);
        Assert.DoesNotContain("every customer", result.Markdown);
        var bindings = result.Projection.Projection!.Blocks.Where(block => block.ProseRef.Contains("#experience-")).ToList();
        Assert.Equal(3, bindings.Count);
        Assert.Single(bindings.Select(block => block.ProseRef).Distinct());
        Assert.All(bindings, block => Assert.Equal("accepted", ledger.Claims.Single(claim => claim.Id == block.ClaimId).Review));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Summary_and_skills_follow_selected_job_evidence_instead_of_reusing_the_whole_career(bool authoredProfileMatches, bool singleAccount)
    {
        var source = ResumeDocument.Create("career.md", "text/markdown", 0);
        source.Personal.Summary = authoredProfileMatches
            ? "Software engineer working with PostgreSQL on reporting and recovery."
            : "Software engineer building internal AI agent runtimes.";
        var originalSummary = source.Personal.Summary;
        source.Experience.Add(new()
        {
            Title = "Engineer",
            Company = "Example",
            IsCurrent = true,
            Technologies = ["PostgreSQL", "Python", "PyTorch"],
            Achievements = ["Designed PostgreSQL reporting queries and database backup recovery drills.",
                "Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms.",
                "Built a Python document-classification prototype using PyTorch; never deployed to production."]
        });
        if (singleAccount) source.Experience[0].Achievements.RemoveAt(1);
        var originalAchievementCount = source.Experience[0].Achievements.Count;
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, Options.Create(new TailoringOptions { ComposeProse = false }),
            NullLogger<SemanticCompressor>.Instance);
        var result = await compressor.CompressAsync(source, new JobDescription
        {
            Title = "Database Platform Engineer",
            RequiredSkills = ["PostgreSQL"],
            Responsibilities = ["Design PostgreSQL reporting queries, database backup recovery drills and optimise measured test latency."]
        }, generationOptions: new() { Detail = ResumeDetail.Thorough });
        Assert.Contains("PostgreSQL", result.Projection.Personal.Summary);
        Assert.False(lucidRESUME.Compiler.CompositionValidator.RepeatsSentence(result.Projection.Personal.Summary!,
            result.Projection.Experience.SelectMany(role => role.Achievements).ToList()));
        Assert.DoesNotContain("AI agent", result.Projection.Personal.Summary);
        Assert.Contains(result.Projection.Skills, skill => skill.Name == "PostgreSQL");
        Assert.DoesNotContain(result.Projection.Skills, skill => skill.Name is "Python" or "PyTorch");
        var summaryClaims = result.Projection.Projection!.Blocks.Where(block => block.ProseRef == "#summary:p1").ToList();
        if (authoredProfileMatches)
        {
            Assert.Equal(originalSummary, result.Projection.Personal.Summary);
            Assert.EndsWith(":personal:summary", Assert.Single(summaryClaims).ClaimId);
        }
        else Assert.True(summaryClaims.Count >= 2);
        Assert.All(summaryClaims, block => Assert.Contains(source.EvidenceLedger!.Claims,
            claim => claim.Id == block.ClaimId && (claim.Kind == "achievement" || claim.Id.EndsWith(":personal:summary", StringComparison.Ordinal)) && claim.Review == "accepted"));
        Assert.Equal(originalSummary, source.Personal.Summary);
        Assert.Equal(originalAchievementCount, source.Experience[0].Achievements.Count);
    }

    private sealed class CountingSearch : IProseSearch
    {
        public int Queries { get; private set; }
        public Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<ProseSearchHit>>([]);
        }
    }

    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
