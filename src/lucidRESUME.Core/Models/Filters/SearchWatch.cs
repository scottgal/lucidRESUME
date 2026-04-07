namespace lucidRESUME.Core.Models.Filters;

/// <summary>
/// A persistent search watch - polls job APIs on a schedule,
/// matches against the skill ledger, and alerts on top matches.
/// Each watch has its own query, hard filters, and polling interval.
/// </summary>
public sealed class SearchWatch
{
    public Guid WatchId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";

    /// <summary>Hard filters applied before scoring.</summary>
    public SearchHardFilter Filters { get; set; } = new();

    /// <summary>Minimum match score (0-1) to trigger notification.</summary>
    public double MinMatchScore { get; set; } = 0.5;

    /// <summary>Polling interval in minutes. 0 = manual only.</summary>
    public int PollIntervalMinutes { get; set; } = 60;

    /// <summary>When this watch was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last time this watch was polled.</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Number of new matches found in last poll.</summary>
    public int LastNewMatches { get; set; }

    /// <summary>Whether this watch is active (will be polled).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether it's time to poll again.</summary>
    public bool IsDue => IsActive && PollIntervalMinutes > 0 &&
        (LastPolledAt is null ||
         (DateTimeOffset.UtcNow - LastPolledAt.Value).TotalMinutes >= PollIntervalMinutes);
}