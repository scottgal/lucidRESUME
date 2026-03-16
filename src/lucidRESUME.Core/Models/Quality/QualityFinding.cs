namespace lucidRESUME.Core.Models.Quality;

public sealed record QualityFinding(
    string Section,       // e.g. "Experience[0].Achievements[2]"
    FindingSeverity Severity,
    string Code,          // e.g. "WEAK_VERB", "MISSING_QUANTITY", "NO_SUMMARY"
    string Message        // human-readable
);
