namespace lucidRESUME.EmailTracker;

public sealed class ScannedEmail
{
    public required string MessageId { get; init; }
    public required string Subject { get; init; }
    public required string From { get; init; }
    public string? FromName { get; init; }
    public DateTimeOffset Date { get; init; }
    public required string BodyPreview { get; init; }
}
