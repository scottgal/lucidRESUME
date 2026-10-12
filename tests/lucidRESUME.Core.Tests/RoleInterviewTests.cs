using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Core.Tests;

public sealed class RoleInterviewTests
{
    [Fact]
    public async Task Aggregate_matching_keeps_other_sources_reviewed_interview_skills_and_rejected_claims_rejected()
    {
        var first = ResumeDocument.Create("first.md", "text/markdown", 0);
        var retained = new WorkExperience
        {
            Company = "Example",
            Title = "Engineer",
            StartDate = new DateOnly(2025, 1, 1),
            IsCurrent = true,
            Achievements = ["Maintained SQL APIs.", "Built recovery workflows.", "Reviewed service designs."]
        };
        first.Experience.Add(retained);
        first.Skills.Add(new Skill { Name = "SQL" });
        var second = ResumeDocument.Create("second.md", "text/markdown", 0);
        var other = new WorkExperience
        {
            Company = "Example",
            Title = "Engineer",
            StartDate = retained.StartDate,
            IsCurrent = true,
            Achievements = ["Supported operations."]
        };
        second.Experience.Add(other);
        second.Skills.Add(new Skill { Name = "Terraform" });
        RoleInterviewRecorder.Accept(second, other.Id, new RoleInterviewEntry
        {
            TopicKey = $"role:{other.Id:N}:scope",
            Action = "Designed tracing",
            ReviewedProse = "Designed OpenTelemetry tracing for service recovery.",
            Skills = ["OpenTelemetry"]
        });
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var skills = new SkillLedgerBuilder(embedder, taxonomy);
        var builder = new CareerRecordJobMlBuilder(skills, taxonomy, embedder);
        var firstFile = await builder.BuildAsync(first);
        first.JobMlSource = new JobMlParser().Serialize(firstFile);
        first.CanonicalMarkdown = firstFile.Markdown;
        var secondFile = await builder.BuildAsync(second);
        foreach (var claim in secondFile.Data.Claims.Where(claim => claim.Statement.Contains("Terraform", StringComparison.OrdinalIgnoreCase) ||
                     claim.Concepts.Skills.Contains("terraform"))) claim.Review = "rejected";
        second.JobMlSource = new JobMlParser().Serialize(secondFile);
        second.CanonicalMarkdown = secondFile.Markdown;
        var state = new lucidRESUME.Core.Persistence.AppState { Resumes = [first, second], SelectedResumeId = first.ResumeId };
        var aggregate = state.BuildAggregateResume()!;
        Assert.Equal(retained.Id, Assert.Single(aggregate.Experience).Id);
        var ledger = await skills.BuildAsync(aggregate);
        Assert.Null(ledger.Find("Terraform"));
        var tracing = ledger.Find("OpenTelemetry");
        Assert.NotNull(tracing);
        Assert.Contains(tracing.Evidence, evidence => evidence.ExperienceId == retained.Id && evidence.SourceText.Contains("OpenTelemetry"));
        Assert.DoesNotContain(RoleInterviewPlanner.PlanCompleteTranscript(aggregate), topic => topic.Key.EndsWith(":scope"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reviewed_answer_round_trips_into_correct_role_without_losing_prior_prose_or_reviews(bool customRoleId)
    {
        var resume = ResumeDocument.Create("original.md", "text/markdown", 0);
        resume.RawMarkdown = "Original source, including a volunteering section.";
        resume.Personal.FullName = "Alex Example";
        var role = new WorkExperience { Title = "Engineer", Company = "Example", Achievements = ["Maintained AWS services."] };
        resume.Experience.Add(role);
        resume.Experience.Add(new WorkExperience { Title = "Analyst", Company = "Earlier" });
        resume.Skills.Add(new Skill { Name = "AWS" });
        var embedder = new EmptyEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
        var original = await builder.BuildAsync(resume);
        original = original with { Markdown = original.Markdown + "\n\nMy carefully authored additional narrative." };
        if (customRoleId)
        {
            var stableId = original.Data.Entities.Single(e => e.Type == "experience" && e.Name.StartsWith("Engineer", StringComparison.Ordinal)).Id;
            var entity = original.Data.Entities.Single(e => e.Id == stableId);
            entity.Id = "custom-role";
            entity.Source = "#custom-role";
            foreach (var claim in original.Data.Claims.Where(c => c.Subject == stableId)) claim.Subject = entity.Id;
            original = original with { Markdown = original.Markdown.Replace("{#" + stableId + "}", "{#custom-role}") };
        }
        resume.JobMlSource = new JobMlParser().Serialize(original);
        resume.CanonicalMarkdown = original.Markdown;
        var rejected = resume.EvidenceLedger.Claims.Single(c => c.Kind == "skill");
        rejected.Review = "rejected";
        var oldRevision = resume.EvidenceLedger.SourceRevision;
        var updated = JsonSerializer.Deserialize<ResumeDocument>(JsonSerializer.Serialize(resume))!;
        var entry = new RoleInterviewEntry
        {
            Skills = ["OpenTelemetry"],
            TopicKey = $"role:{role.Id:N}:delivery",
            Requirement = "AWS",
            Context = "During recurring production incidents",
            Action = "Automated recovery",
            Outcome = "Reduced outages",
            ReviewedProse = "Reduced AWS outages by automating recovery."
        };
        Assert.True(RoleInterviewRecorder.Accept(updated, role.Id, entry));
        Assert.False(RoleInterviewRecorder.Accept(updated, role.Id, entry));
        Assert.NotEqual(oldRevision, updated.EvidenceLedger.SourceRevision);
        Assert.Equal("rejected", updated.EvidenceLedger.Claims.Single(c => c.Id == rejected.Id).Review);
        Assert.Contains(updated.EvidenceLedger.Evidence, e => e.Text == entry.ReviewedProse && e.ExtractionMethod == "author-interview");
        var career = RoleInterviewTranscriptUpdater.Merge(resume, await builder.BuildAsync(updated), role.Id, entry.ReviewedProse);
        Assert.NotNull(career.Data.Extensions);
        var completeSource = new JobMlParser().Serialize(career);
        Assert.Contains("role_interviews:", completeSource);
        Assert.Contains(entry.Context, completeSource);
        var interviewClaim = Assert.Single(career.Data.Claims, c => c.Statement == entry.ReviewedProse);
        var interviewSource = Assert.Single(career.Data.Sources, source => source.Type == "interview");
        var skillClaim = Assert.Single(career.Data.Claims, claim => claim.Type == "technology" && claim.Statement == "OpenTelemetry");
        Assert.Equal(interviewClaim.Subject, skillClaim.Subject);
        Assert.Contains(skillClaim.Evidence, evidence => evidence.SourceId == interviewSource.Id);

        Assert.Contains(interviewClaim.Evidence, e => e.SourceId == interviewSource.Id);
        Assert.DoesNotContain(interviewClaim.Evidence, e => e.SourceId is not null &&
            career.Data.Sources.Any(source => source.Id == e.SourceId && source.Type == "resume"));
        Assert.Equal(2, career.Data.Entities.Count(e => e.Type == "experience"));
        if (customRoleId) Assert.Equal("custom-role", interviewClaim.Subject);
        updated.CanonicalMarkdown = career.Markdown;
        updated.JobMlSource = new JobMlParser().Serialize(career);
        var reloaded = JsonSerializer.Deserialize<ResumeDocument>(JsonSerializer.Serialize(updated))!;
        Assert.Equal(resume.RawMarkdown, reloaded.RawMarkdown);
        Assert.Contains("My carefully authored additional narrative.", reloaded.CanonicalMarkdown);
        Assert.Contains(entry.ReviewedProse, reloaded.CanonicalMarkdown);
        Assert.Single(reloaded.Experience.Single(r => r.Id == role.Id).InterviewEntries);
        Assert.Empty(reloaded.Experience.Single(r => r.Id != role.Id).InterviewEntries);
        Assert.Contains("OpenTelemetry", reloaded.Experience.Single(r => r.Id == role.Id).Technologies);
        var state = new lucidRESUME.Core.Persistence.AppState { Resumes = [reloaded] };
        Assert.Equal(entry.TopicKey, Assert.Single(state.BuildAggregateResume()!.Experience
            .Single(r => r.Id == role.Id).InterviewEntries).TopicKey);
        var skills = await new SkillLedgerBuilder(embedder).BuildAsync(reloaded);
        Assert.Contains(skills.Entries.Single(skill => skill.SkillName.Equals("OpenTelemetry", StringComparison.OrdinalIgnoreCase)).Evidence,
            evidence => evidence.ExperienceId == role.Id);

        Assert.DoesNotContain(JobMlProcessor.Validate(new JobMlParser().Parse(reloaded.JobMlSource!)),
            d => d.Severity == JobMlDiagnosticSeverity.Error);
        Assert.All(JobMlProcessor.Reconcile(career).SelectMany(c => c.Evidence),
            evidence => Assert.True(evidence.State is EvidenceState.Valid or EvidenceState.External));
    }

    private sealed class EmptyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
