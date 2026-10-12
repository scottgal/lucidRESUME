using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

/// <summary>Conservative source-conflict triggers for author review, never inferred reconciliations.</summary>
public static class RoleEvidenceQuality
{
    public static bool HasRoleIdentity(WorkExperience role) => WorkExperienceIdentity.IsValid(role);

    public static bool NeedsStatusClarification(WorkExperience role) =>
        !role.InterviewEntries.Any(entry => entry.TopicKey.StartsWith("role:", StringComparison.Ordinal) &&
            entry.TopicKey.EndsWith(":status", StringComparison.Ordinal)) &&
        role.Achievements.Any(HasCancellation) && role.Achievements.Any(HasMilestone);

    public static bool IsStatusSensitive(string text) => HasCancellation(text) || HasMilestone(text);
    public static bool HasConversationalAside(string text) => Regex.IsMatch(text,
        @"\b(unfortunately|alas)\b|etc\.\.\.", RegexOptions.IgnoreCase);
    private static bool HasCancellation(string text) => Regex.IsMatch(text,
        @"\b(no longer required|cancelled|canceled|abandoned|changed product direction)\b", RegexOptions.IgnoreCase);
    private static bool HasMilestone(string text) => Regex.IsMatch(text,
        @"\b(deliver(?:ed|ing)?\s+(?:an?\s+)?MVP|launched|went live)\b", RegexOptions.IgnoreCase);
}
