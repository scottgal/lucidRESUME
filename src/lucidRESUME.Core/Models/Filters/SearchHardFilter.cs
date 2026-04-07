namespace lucidRESUME.Core.Models.Filters;

/// <summary>
/// Hard filters for job search - non-negotiable requirements.
/// Applied before matching/scoring. Jobs that fail any hard filter are excluded.
/// </summary>
public sealed class SearchHardFilter
{
    /// <summary>Must display salary (exclude listings without salary info).</summary>
    public bool RequireSalary { get; set; }

    /// <summary>Minimum salary (in user's preferred currency).</summary>
    public decimal? MinSalary { get; set; }

    /// <summary>Contract type filter: "permanent", "contract", "freelance".</summary>
    public List<string> ContractTypes { get; set; } = [];

    /// <summary>Must be remote.</summary>
    public bool RequireRemote { get; set; }

    /// <summary>Must be in one of these locations.</summary>
    public List<string> Locations { get; set; } = [];

    /// <summary>Exclude jobs from these companies.</summary>
    public List<string> ExcludedCompanies { get; set; } = [];

    /// <summary>Exclude jobs older than this many days.</summary>
    public int? MaxAgeDays { get; set; }

    /// <summary>Minimum number of skills matching your profile.</summary>
    public int? MinSkillMatch { get; set; }

    /// <summary>Apply all hard filters to a job. Returns true if job passes.</summary>
    public bool Passes(Jobs.JobDescription job, decimal? detectedSalaryMin = null)
    {
        if (RequireSalary && job.Salary is null) return false;
        if (MinSalary.HasValue && (job.Salary?.Min ?? 0) < MinSalary.Value) return false;
        if (RequireRemote && job.IsRemote != true) return false;
        if (MaxAgeDays.HasValue && (DateTimeOffset.UtcNow - job.CreatedAt).TotalDays > MaxAgeDays.Value) return false;

        if (ExcludedCompanies.Count > 0 && job.Company != null &&
            ExcludedCompanies.Any(c => c.Equals(job.Company, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (Locations.Count > 0 && job.Location != null &&
            !Locations.Any(l => job.Location.Contains(l, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}