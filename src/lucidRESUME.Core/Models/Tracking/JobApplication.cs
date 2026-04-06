namespace lucidRESUME.Core.Models.Tracking;

public sealed class JobApplication
{
    public Guid ApplicationId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }

    public ApplicationStage Stage { get; set; } = ApplicationStage.Saved;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? NextFollowUp { get; set; }

    public ContactInfo Contact { get; set; } = new();
    public List<TimelineEvent> Timeline { get; set; } = [];
    public string? UserNotes { get; set; }

    // Link to tailored resume version used for this application
    public Guid? TailoredResumeId { get; set; }

    // Denormalized for fast display without joining to JobDescription
    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }

    public void AdvanceTo(ApplicationStage newStage)
    {
        var evt = new TimelineEvent
        {
            Type = TimelineEventType.StageChange,
            Title = $"{Stage} \u2192 {newStage}",
            FromStage = Stage,
            ToStage = newStage
        };
        Timeline.Add(evt);
        Stage = newStage;
        LastActivityAt = DateTimeOffset.UtcNow;
        if (newStage == ApplicationStage.Applied && AppliedAt is null)
            AppliedAt = DateTimeOffset.UtcNow;
    }

    public void AddNote(string text)
    {
        Timeline.Add(new TimelineEvent
        {
            Type = TimelineEventType.NoteAdded,
            Title = "Note",
            Description = text
        });
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public static JobApplication Create(Jobs.JobDescription job)
    {
        return new JobApplication
        {
            JobId = job.JobId,
            CompanyName = job.Company,
            JobTitle = job.Title,
        };
    }
}
