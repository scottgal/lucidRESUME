namespace lucidRESUME.Core.Models.Tracking;

public sealed class TimelineEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public TimelineEventType Type { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    // For email-sourced events
    public string? EmailMessageId { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailFrom { get; set; }

    // For stage changes
    public ApplicationStage? FromStage { get; set; }
    public ApplicationStage? ToStage { get; set; }

    // True when created by email scanner rather than user
    public bool IsAutoDetected { get; set; }
}
