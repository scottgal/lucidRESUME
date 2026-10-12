using lucidRESUME.Core.Models.Evidence;

namespace lucidRESUME.Core.Models.Resume;

public static class RoleInterviewRecorder
{
    /// <summary>Call only after the author reviews the prose. Appends without rewriting prior claims.</summary>
    public static bool Accept(ResumeDocument resume, Guid roleId, RoleInterviewEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ReviewedProse);
        var role = resume.Experience.Single(r => r.Id == roleId);
        if (role.InterviewEntries.Any(e => e.Id == entry.Id)) return false;
        entry.ReviewedProse = entry.ReviewedProse.Trim();
        if (role.Achievements.Contains(entry.ReviewedProse, StringComparer.Ordinal)) return false;
        entry.AcceptedAt = DateTimeOffset.UtcNow;
        entry.Skills = entry.Skills.Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => skill.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        entry.Account = (entry.Account ?? new ContributionAccount()) with
        {
            RoleId = roleId,
            InterviewId = entry.Id,
            Action = entry.Action,
            Context = entry.Context,
            Outcome = entry.Outcome,
            ReviewedProse = entry.ReviewedProse,
            Skills = [.. entry.Skills]
        };
        foreach (var skill in entry.Skills)
            if (!role.Technologies.Contains(skill, StringComparer.OrdinalIgnoreCase)) role.Technologies.Add(skill);
        role.InterviewEntries.Add(entry);
        role.Achievements.Add(entry.ReviewedProse);
        resume.LastModifiedAt = entry.AcceptedAt;
        EvidenceLedgerBuilder.EnsureCurrent(resume);
        return true;
    }
}
