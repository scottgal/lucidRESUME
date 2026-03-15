namespace lucidRESUME.Core.Models.Jobs;

public enum JobSourceType { PastedText, Url, Adzuna, Remotive, Reed, Indeed, LinkedIn, Arbeitnow, JoinRise, Jobicy, Findwork }

public sealed class JobSource
{
    public JobSourceType Type { get; init; }
    public string? Url { get; init; }
    public string? ApiName { get; init; }
    public string? ExternalId { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
}
