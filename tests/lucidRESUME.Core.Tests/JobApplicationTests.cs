using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.Core.Tests;

public class JobApplicationTests
{
    [Fact]
    public void Create_FromJobDescription_CopiesDenormalizedFields()
    {
        var job = JobDescription.Create("Some raw text", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Senior Dev";
        job.Company = "Acme Corp";

        var app = JobApplication.Create(job);

        Assert.Equal(job.JobId, app.JobId);
        Assert.Equal("Senior Dev", app.JobTitle);
        Assert.Equal("Acme Corp", app.CompanyName);
        Assert.Equal(ApplicationStage.Saved, app.Stage);
        Assert.Empty(app.Timeline);
    }

    [Fact]
    public void AdvanceTo_LogsStageChangeEvent()
    {
        var app = new JobApplication();
        Assert.Equal(ApplicationStage.Saved, app.Stage);

        app.AdvanceTo(ApplicationStage.Applied);

        Assert.Equal(ApplicationStage.Applied, app.Stage);
        Assert.Single(app.Timeline);
        Assert.Equal(TimelineEventType.StageChange, app.Timeline[0].Type);
        Assert.Equal(ApplicationStage.Saved, app.Timeline[0].FromStage);
        Assert.Equal(ApplicationStage.Applied, app.Timeline[0].ToStage);
        Assert.NotNull(app.AppliedAt);
        Assert.NotNull(app.LastActivityAt);
    }

    [Fact]
    public void AdvanceTo_Applied_SetsAppliedAtOnce()
    {
        var app = new JobApplication();
        app.AdvanceTo(ApplicationStage.Applied);
        var firstAppliedAt = app.AppliedAt;

        app.AdvanceTo(ApplicationStage.Screening);
        Assert.Equal(firstAppliedAt, app.AppliedAt); // should not change
    }

    [Fact]
    public void AdvanceTo_MultipleStages_BuildsTimeline()
    {
        var app = new JobApplication();
        app.AdvanceTo(ApplicationStage.Applied);
        app.AdvanceTo(ApplicationStage.Screening);
        app.AdvanceTo(ApplicationStage.Interview);

        Assert.Equal(3, app.Timeline.Count);
        Assert.Equal(ApplicationStage.Interview, app.Stage);
    }

    [Fact]
    public void AddNote_CreatesTimelineEvent()
    {
        var app = new JobApplication();
        app.AddNote("Spoke with recruiter");

        Assert.Single(app.Timeline);
        Assert.Equal(TimelineEventType.NoteAdded, app.Timeline[0].Type);
        Assert.Equal("Spoke with recruiter", app.Timeline[0].Description);
        Assert.NotNull(app.LastActivityAt);
    }

    [Fact]
    public void AdvanceTo_Rejected_SetsStage()
    {
        var app = new JobApplication();
        app.AdvanceTo(ApplicationStage.Applied);
        app.AdvanceTo(ApplicationStage.Rejected);

        Assert.Equal(ApplicationStage.Rejected, app.Stage);
        Assert.Equal(2, app.Timeline.Count);
    }
}
