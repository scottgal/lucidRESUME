using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Models.Skills;

/// <summary>
/// The complete skill ledger for a resume - every skill claim backed by evidence.
/// Built by scanning all sections of a ResumeDocument and cross-referencing dates.
/// </summary>
public sealed class SkillLedger
{
    public List<SkillLedgerEntry> Entries { get; set; } = [];

    /// <summary>Skills with strong evidence (strength > 0.5).</summary>
    public IReadOnlyList<SkillLedgerEntry> StrongSkills =>
        Entries.Where(e => e.Strength > 0.5).OrderByDescending(e => e.Strength).ToList();

    /// <summary>Skills with weak evidence (mentioned but thin).</summary>
    public IReadOnlyList<SkillLedgerEntry> WeakSkills =>
        Entries.Where(e => e.Strength <= 0.5 && e.Strength > 0.1).OrderByDescending(e => e.Strength).ToList();

    /// <summary>Skills mentioned only in skills section with no experience evidence.</summary>
    public IReadOnlyList<SkillLedgerEntry> UnsubstantiatedSkills =>
        Entries.Where(e => e.Evidence.All(ev => ev.Source == EvidenceSource.SkillsSection)).ToList();

    /// <summary>
    /// Consistency issues: claimed years don't match calculated years,
    /// skills listed but never used in experience, etc.
    /// </summary>
    public List<ConsistencyIssue> Issues { get; set; } = [];

    /// <summary>Find a skill by name (case-insensitive).</summary>
    public SkillLedgerEntry? Find(string skillName) =>
        Entries.FirstOrDefault(e => e.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase));
}

public sealed class ConsistencyIssue
{
    public string SkillName { get; set; } = "";
    public string Description { get; set; } = "";
    public ConsistencySeverity Severity { get; set; }
}

public enum ConsistencySeverity
{
    Info,       // Minor observation
    Warning,    // Potential issue worth reviewing
    Error       // Factual inconsistency
}