using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching.Tests;

public sealed class RoleInterviewPlannerTests
{
    [Fact]
    public void Malformed_import_headers_are_retained_but_not_interviewed_as_real_employment()
    {
        var resume = ResumeDocument.Create("legacy.md", "text/markdown", 0);
        resume.Experience.Add(new WorkExperience { Title = "Engineer", Company = "Example" });
        resume.Experience.Add(new WorkExperience { Title = "Legacy company title — Example", Company = "Remote Jan" });
        resume.Experience.Add(new WorkExperience { Title = "Jan 2007 – Oct 2009 Redmond, WA" });
        var topics = RoleInterviewPlanner.PlanCompleteTranscript(resume);
        Assert.Equal(6, topics.Count);
        Assert.All(topics, topic => Assert.Equal(resume.Experience[0].Id, topic.SuggestedRoleId));
        Assert.Equal(3, resume.Experience.Count);
    }

    [Fact]
    public void Conflicting_delivery_and_cancellation_accounts_require_status_clarification()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var role = new WorkExperience { Title = "Lead", Company = "Example", Achievements =
            ["Led a team delivering MVP within 90 days.", "They changed product direction and the team was no longer required."] };
        resume.Experience.Add(role);
        Assert.True(RoleEvidenceQuality.NeedsStatusClarification(role));
        var status = Assert.Single(RoleInterviewPlanner.PlanCompleteTranscript(resume), topic => topic.Key.EndsWith(":status"));
        Assert.Contains("actually completed", status.Prompt);
        role.InterviewEntries.Add(new RoleInterviewEntry { TopicKey = status.Key, ReviewedProse = "Clarified timeline." });
        Assert.False(RoleEvidenceQuality.NeedsStatusClarification(role));
        Assert.Equal(6, RoleInterviewPlanner.PlanCompleteTranscript(resume).Count);
    }

    [Fact]
    public void Complete_transcript_interview_resumes_each_roles_unanswered_dimensions()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var role = new WorkExperience { Title = "Engineer", Company = "Example", IsCurrent = true };
        resume.Experience.Add(role);
        var plan = RoleInterviewPlanner.PlanCompleteTranscript(resume);
        Assert.Equal(6, plan.Count);
        Assert.All(plan, topic => Assert.Equal(role.Id, topic.SuggestedRoleId));
        var first = plan[0];
        role.InterviewEntries.Add(new RoleInterviewEntry { TopicKey = first.Key });
        var resumed = RoleInterviewPlanner.PlanCompleteTranscript(resume);
        Assert.Equal(5, resumed.Count);
        Assert.DoesNotContain(resumed, topic => topic.Key == first.Key);
        Assert.Contains(resumed, topic => topic.Prompt!.Contains("prototype, pilot or production"));
    }

    [Fact]
    public void Verbose_duties_prompt_for_outcomes_but_concise_results_do_not()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var role = new WorkExperience { Company = "Example", Title = "Engineer" };
        resume.Experience.Add(role);
        var duties = "Responsible for the ongoing maintenance and day to day operation of all production AWS systems.";
        var report = new CoverageReport([
            new(new("AWS", RequirementPriority.Required), duties, "Experience[0].Achievements[0]", 1) { IsDirectMatch = true }],
            CompanyType.Unknown, DateTimeOffset.UtcNow);
        Assert.Equal(role.Id, Assert.Single(RoleInterviewPlanner.Plan(resume, report)).SuggestedRoleId);
        var concise = report with { Entries = [report.Entries[0] with { Evidence = "Reduced AWS outages by automating recovery." }] };
        Assert.Empty(RoleInterviewPlanner.Plan(resume, concise));
    }

    [Fact]
    public void Missing_evidence_does_not_assign_a_role_or_drop_unanswered_requirements()
    {
        var resume = ResumeDocument.Create("cv.md", "text/markdown", 0);
        var entries = Enumerable.Range(0, 20).Select(i => new CoverageEntry(
            new("Missing skill " + i, RequirementPriority.Required), null, null, 0)).ToList();
        var report = new CoverageReport(entries, CompanyType.Unknown, DateTimeOffset.UtcNow);
        var topics = RoleInterviewPlanner.Plan(resume, report);
        Assert.Equal(20, topics.Count);
        Assert.All(topics, topic => Assert.Null(topic.SuggestedRoleId));
    }
}
