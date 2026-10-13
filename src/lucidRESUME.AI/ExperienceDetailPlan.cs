using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Allocates space to evidence, and counts dated employment once across concurrent roles.</summary>
public sealed record ExperienceDetailPlan(
    IReadOnlyDictionary<Guid, int> AchievementLimits,
    IReadOnlyList<Guid> ChronologyRoleIds,
    int? RequiredYears,
    double EvidencedYears)
{
    public static ExperienceDetailPlan Build(JobDescription job, IReadOnlyList<WorkExperience> roles,
        IReadOnlySet<Guid> detailedIds, IReadOnlyDictionary<Guid, double> scores,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>> requirements, ResumeGenerationBudget budget,
        DateOnly today)
    {
        var requiredYears = job.RequiredYearsExperience;
        if (requiredYears is null)
        {
            // Extract overall professional tenure only. A skill-specific duration is not career tenure.
            var match = Regex.Match(job.RawText, @"\b(\d{1,2})\s*\+?\s*years?\s+(?:of\s+)?(?:professional\s+|commercial\s+|relevant\s+)?(?:software\s+engineering|engineering|professional|work|industry)\s+experience\b", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var years)) requiredYears = years;
        }
        if (requiredYears <= 0) requiredYears = null;
        var detailed = roles.Where(role => detailedIds.Contains(role.Id)).ToList();
        var limits = detailed.ToDictionary(role => role.Id, _ => 1);
        var remaining = Math.Max(0, detailed.Count * budget.AchievementsPerRole - detailed.Count);
        var represented = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var role in detailed)
            foreach (var requirement in requirements.GetValueOrDefault(role.Id) ?? new HashSet<string>())
                represented[requirement] = represented.GetValueOrDefault(requirement) + 1;
        double Weight(WorkExperience role)
        {
            var matches = requirements.GetValueOrDefault(role.Id) ?? new HashSet<string>();
            var unique = matches.Count(id => represented[id] == 1);
            var age = Math.Max(0, today.Year - (role.IsCurrent ? today.Year : role.EndDate?.Year ?? role.StartDate?.Year ?? today.Year));
            return Math.Max(0, scores.GetValueOrDefault(role.Id)) + matches.Count * 4 + unique * 12 + Math.Max(0, 8 - age);
        }
        // Diminishing returns distribute a finite total budget; distinctive older evidence can earn space.
        var maximum = Math.Max(1, budget.AchievementsPerRole + 2);
        while (remaining-- > 0)
        {
            var next = detailed.Where(role => limits[role.Id] < maximum &&
                    limits[role.Id] < Math.Max(1, role.Achievements.Count + role.InterviewEntries.Count))
                .OrderByDescending(role => Weight(role) / (limits[role.Id] + 1))
                .ThenByDescending(role => role.StartDate).ThenBy(role => role.Id).FirstOrDefault();
            if (next is null) break;
            limits[next.Id]++;
        }
        var chronology = new List<WorkExperience>(detailed);
        var additional = new List<Guid>();
        var candidates = roles.Where(role => !detailedIds.Contains(role.Id)).ToList();
        // Extend the chronology with the roles adding real, non-overlapping tenure first.
        while (requiredYears is > 0 && CoveredYears(chronology, today) + .01 < requiredYears && candidates.Count > 0)
        {
            var covered = CoveredYears(chronology, today);
            var next = candidates.OrderByDescending(role => CoveredYears(chronology.Append(role), today) - covered)
                .ThenByDescending(role => scores.GetValueOrDefault(role.Id)).ThenByDescending(role => role.StartDate).First();
            if (CoveredYears(chronology.Append(next), today) <= covered) break;
            chronology.Add(next); additional.Add(next.Id); candidates.Remove(next);
        }
        foreach (var role in candidates.OrderByDescending(role => role.StartDate).Take(Math.Max(0, 6 - additional.Count)))
        { chronology.Add(role); additional.Add(role.Id); }
        return new(limits, additional, requiredYears, CoveredYears(chronology, today));
    }

    internal static double CoveredYears(IEnumerable<WorkExperience> roles, DateOnly today)
    {
        var intervals = roles.Where(role => role.StartDate.HasValue)
            .Select(role => (Start: role.StartDate!.Value.DayNumber,
                End: role.IsCurrent ? today.DayNumber : role.EndDate is { } endDate
                    ? role.EndDateIsMonthOnly
                        ? new DateOnly(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month)).DayNumber + 1
                        : endDate.DayNumber + 1
                    : role.StartDate.Value.DayNumber))
            .Where(interval => interval.Start < interval.End && interval.Start <= today.DayNumber)
            .Select(interval => (interval.Start, End: Math.Min(interval.End, today.DayNumber)))
            .OrderBy(interval => interval.Start).ToList();
        var days = 0; var end = 0;
        foreach (var interval in intervals)
        { days += Math.Max(0, interval.End - Math.Max(end, interval.Start)); end = Math.Max(end, interval.End); }
        return days / 365.2425;
    }
}
