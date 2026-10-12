using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Resume;

public static class WorkExperienceIdentity
{
    public static bool IsValid(WorkExperience role) =>
        !string.IsNullOrWhiteSpace(role.Title) && !string.IsNullOrWhiteSpace(role.Company) &&
        !Regex.IsMatch(role.Company, @"^(?:Remote|[^\r\n]+,\s*(?:UK|US|USA|WA))\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(role.Company, @"^\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}$") &&
        !role.Title.StartsWith("Present ", StringComparison.OrdinalIgnoreCase);
}
