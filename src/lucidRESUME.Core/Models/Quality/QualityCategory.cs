namespace lucidRESUME.Core.Models.Quality;

public sealed record QualityCategory(
    string Name,
    int Score,       // 0-100
    int Weight,      // relative weight e.g. 35
    IReadOnlyList<QualityFinding> Findings
);
