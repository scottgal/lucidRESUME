using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Matching.Tests;

public sealed class ConservativeSkillMatchingTests
{
    [Theory]
    [InlineData("Python", "financial modeling")]
    [InlineData("Terraform", "infrastructure as code")]
    [InlineData("AWS", "EC2")]
    [InlineData("Kubernetes", "Helm")]
    [InlineData("Docker", "containerization")]
    [InlineData("Java", "JavaScript")]
    [InlineData("Go", "Django")]
    [InlineData("C", "C++")]
    [InlineData("GAAP", "IFRS")]
    [InlineData("release ownership", "release management")]
    public async Task Related_concepts_never_satisfy_requirements_even_at_high_similarity(string required, string candidate)
    {
        Assert.False(SkillTaxonomy.AreEquivalent(required, candidate));
        var result = await new SkillLedgerMatcher(new SimilarEmbedder()).MatchAsync(Ledger(candidate), Requirements(required));
        var match = Assert.Single(result.Matches);
        Assert.False(match.IsMatched);
        Assert.Null(match.MatchedResumeSkill);
        Assert.Equal(candidate, match.SuggestedResumeSkill);
        Assert.Equal(SkillMatchKind.Related, match.MatchKind);
        Assert.Equal(0, result.RequiredCoverage);
        Assert.Contains(required, result.Gaps);
        Assert.Equal(0, match.CalculatedYears);
        Assert.Equal(0, match.EvidenceStrength);
        Assert.Equal(0, match.EvidenceCount);
        Assert.Equal(candidate, Assert.Single(result.NearMisses).ClosestResumeSkill);
    }

    [Theory]
    [InlineData("Kubernetes", "k8s")]
    [InlineData("AWS", "Amazon Web Services")]
    [InlineData("Go", "Golang")]
    [InlineData("C#", "CSharp")]
    [InlineData("IaC", "infrastructure as code")]
    public async Task Audited_synonyms_match_without_embedding_service(string required, string candidate)
    {
        var result = await new SkillLedgerMatcher(new FailingEmbedder()).MatchAsync(Ledger(candidate), Requirements(required));
        Assert.True(Assert.Single(result.Matches).IsMatched);
        Assert.Equal(1, result.RequiredCoverage);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public async Task Retrieval_failure_keeps_related_concept_as_an_interview_suggestion()
    {
        var result = await new SkillLedgerMatcher(new FailingEmbedder()).MatchAsync(Ledger("infrastructure as code"), Requirements("Terraform"));
        Assert.Equal("infrastructure as code", Assert.Single(result.NearMisses).ClosestResumeSkill);
        Assert.Equal(0, result.RequiredCoverage);
    }

    [Fact]
    public async Task Keyword_overlap_cannot_borrow_an_unrelated_skills_years()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Experience.Add(new WorkExperience { Achievements = ["Supported a JavaScript migration with the product team."] });
        var result = await new SkillLedgerMatcher(new FailingEmbedder()).MatchAsync(Ledger("SQL"), Requirements("Java"), resumeDoc: resume);
        var match = Assert.Single(result.Matches);
        Assert.False(match.IsMatched);
        Assert.Equal(0, match.CalculatedYears);
        Assert.Equal(0, match.EvidenceCount);
    }

    [Fact]
    public async Task Similar_role_prose_cannot_inflate_skill_years()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Skills.Add(new Skill { Name = "Python" });
        resume.Experience.Add(new WorkExperience
        {
            StartDate = new DateOnly(2010, 1, 1), EndDate = new DateOnly(2020, 1, 1),
            Achievements = ["Built financial modeling spreadsheets for annual investment planning."]
        });
        var ledger = await new SkillLedgerBuilder(new SimilarEmbedder()).BuildAsync(resume);
        var python = Assert.Single(ledger.Entries, entry => entry.SkillName.Equals("Python", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, python.CalculatedYears);
        Assert.DoesNotContain(python.Evidence, evidence => evidence.Source == EvidenceSource.AchievementBullet);
    }

    [Fact]
    public async Task Semantic_candidate_remains_a_coverage_gap_but_keeps_passage_for_interview()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        resume.Experience.Add(new WorkExperience { Achievements = ["Automated infrastructure as code with CloudFormation."] });
        var job = JobDescription.Create("Engineer", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills.Add("Terraform");
        var analyser = new ResumeCoverageAnalyser(new CompanyClassifier(Options.Create(new CompanyClassifierOptions())),
            Options.Create(new CoverageOptions()), new SimilarEmbedder());
        var report = await analyser.AnalyseAsync(resume, job);
        var gap = Assert.Single(report.RequiredGaps);
        Assert.NotNull(gap.Evidence);
        Assert.False(gap.IsDirectMatch);
        Assert.Equal(0, report.CoveragePercent);
    }

    [Fact]
    public void Mention_extraction_does_not_turn_related_terms_into_named_tools()
    {
        var concepts = SkillTaxonomy.FindCanonicalMentions("Financial modeling and infrastructure as code with Helm and containerization.");
        Assert.DoesNotContain("python", concepts);
        Assert.DoesNotContain("terraform", concepts);
        Assert.DoesNotContain("kubernetes", concepts);
        Assert.DoesNotContain("docker", concepts);
    }

    [Fact]
    public async Task Taxonomy_suggestion_keeps_its_own_score_and_remains_actionable_offline()
    {
        var ledger = Ledger("infrastructure as code");
        ledger.Entries.Add(Ledger("SQL").Entries[0]);
        var result = await new SkillLedgerMatcher(new DifferentScoresEmbedder()).MatchAsync(ledger, Requirements("Terraform"));
        var near = Assert.Single(result.NearMisses);
        Assert.Equal("infrastructure as code", near.ClosestResumeSkill);
        Assert.Equal(.1f, near.Similarity);
        var plan = await new lucidRESUME.Matching.Graph.CareerPlanner(new FailingEmbedder())
            .PlanAsync(ledger, Requirements("Terraform"), new lucidRESUME.Matching.Graph.SkillGraph());
        var advice = Assert.Single(plan.Recommendations);
        Assert.Equal(lucidRESUME.Matching.Graph.GapType.AdjacentSkill, advice.GapType);
        Assert.Contains("keep this as a gap", advice.Advice);
    }

    private sealed class DifferentScoresEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new float[] { text == "infrastructure as code" ? .1f : .99f });
        public float CosineSimilarity(float[] a, float[] b) => b[0];
    }

    private static SkillLedger Ledger(string skill) => new()
    {
        Entries = [new SkillLedgerEntry { SkillName = skill, CalculatedYears = 12,
            Evidence = [new SkillEvidence { SourceText = skill, Confidence = 1, Source = EvidenceSource.SkillsSection }] }]
    };
    private static JdSkillLedger Requirements(string skill) => new()
    { Requirements = [new JdSkillRequirement { SkillName = skill, Importance = SkillImportance.Required, Embedding = [99, 99, 99] }] };
    private sealed class SimilarEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(new float[] { 1, 0 });
        public float CosineSimilarity(float[] a, float[] b) => 0.99f;
    }
    private sealed class FailingEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => throw new HttpRequestException("Offline");
        public float CosineSimilarity(float[] a, float[] b) => throw new InvalidOperationException();
    }
}
