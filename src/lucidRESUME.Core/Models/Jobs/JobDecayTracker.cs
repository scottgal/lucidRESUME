namespace lucidRESUME.Core.Models.Jobs;

/// <summary>
/// Tracks job listing freshness and company application history.
/// Jobs decay over time (stale listings). Companies track contact frequency
/// to avoid over-applying.
/// </summary>
public sealed class JobDecayTracker
{
    /// <summary>How many days before a job listing is considered stale.</summary>
    public int StaleDays { get; set; } = 30;

    /// <summary>How many days before a job listing is considered expired.</summary>
    public int ExpiredDays { get; set; } = 90;

    /// <summary>Minimum days between applications to the same company.</summary>
    public int CompanyCooldownDays { get; set; } = 14;

    public JobFreshness GetFreshness(JobDescription job)
    {
        var age = (DateTimeOffset.UtcNow - job.CreatedAt).TotalDays;
        if (age > ExpiredDays) return JobFreshness.Expired;
        if (age > StaleDays) return JobFreshness.Stale;
        if (age > 7) return JobFreshness.Aging;
        return JobFreshness.Fresh;
    }

    public bool IsCompanyOnCooldown(string company, IReadOnlyList<Tracking.JobApplication> applications)
    {
        var lastApplied = applications
            .Where(a => a.CompanyName?.Equals(company, StringComparison.OrdinalIgnoreCase) == true)
            .Where(a => a.Stage >= Tracking.ApplicationStage.Applied)
            .OrderByDescending(a => a.AppliedAt)
            .FirstOrDefault();

        if (lastApplied?.AppliedAt is null) return false;
        return (DateTimeOffset.UtcNow - lastApplied.AppliedAt.Value).TotalDays < CompanyCooldownDays;
    }
}

public enum JobFreshness
{
    Fresh,    // < 7 days old
    Aging,    // 7-30 days
    Stale,    // 30-90 days
    Expired,  // > 90 days
}
