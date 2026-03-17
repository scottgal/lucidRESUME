namespace lucidRESUME.Core.Models.Coverage;

/// <summary>
/// Maps one JD requirement to the best-matching evidence in the resume,
/// or null if no evidence was found (a gap).
/// </summary>
public sealed record CoverageEntry(
    JdRequirement Requirement,
    string? Evidence,          // null = gap
    string? EvidenceSection,   // e.g. "Experience[0].Achievements[2]"
    float Score                // 0–1 match confidence
)
{
    public bool IsCovered => Evidence is not null;
}
