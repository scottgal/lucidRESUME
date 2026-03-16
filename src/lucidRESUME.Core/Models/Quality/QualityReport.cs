namespace lucidRESUME.Core.Models.Quality;

public sealed record QualityReport(
    int OverallScore,                           // 0-100, weighted average
    IReadOnlyList<QualityCategory> Categories,
    DateTimeOffset GeneratedAt
)
{
    public IEnumerable<QualityFinding> AllFindings =>
        Categories.SelectMany(c => c.Findings);

    public IEnumerable<QualityFinding> Errors =>
        AllFindings.Where(f => f.Severity == FindingSeverity.Error);

    public IEnumerable<QualityFinding> Warnings =>
        AllFindings.Where(f => f.Severity == FindingSeverity.Warning);
}
