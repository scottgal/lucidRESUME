using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Models.Skills;

/// <summary>
/// Skill ledger for a job description - the demand side.
/// Maps to the resume's SkillLedger (supply side) for matching.
/// </summary>
public sealed class JdSkillLedger
{
    public Guid JobId { get; set; }
    public string? JobTitle { get; set; }
    public string? Company { get; set; }

    public List<JdSkillRequirement> Requirements { get; set; } = [];

    public IReadOnlyList<JdSkillRequirement> Required =>
        Requirements.Where(r => r.Importance == SkillImportance.Required).ToList();

    public IReadOnlyList<JdSkillRequirement> Preferred =>
        Requirements.Where(r => r.Importance == SkillImportance.Preferred).ToList();

    public IReadOnlyList<JdSkillRequirement> Inferred =>
        Requirements.Where(r => r.Importance == SkillImportance.Inferred).ToList();
}