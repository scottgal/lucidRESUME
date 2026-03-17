using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Models.Coverage;

public sealed record CoverageReport(
    IReadOnlyList<CoverageEntry> Entries,
    CompanyType CompanyType,
    DateTimeOffset GeneratedAt)
{
    public IEnumerable<CoverageEntry> Gaps         => Entries.Where(e => !e.IsCovered);
    public IEnumerable<CoverageEntry> RequiredGaps => Gaps.Where(e => e.Requirement.Priority == RequirementPriority.Required);
    public IEnumerable<CoverageEntry> Covered      => Entries.Where(e => e.IsCovered);

    /// <summary>0–100 overall coverage percentage.</summary>
    public int CoveragePercent => Entries.Count == 0 ? 0
        : (int)(100.0 * Covered.Count() / Entries.Count);
}
