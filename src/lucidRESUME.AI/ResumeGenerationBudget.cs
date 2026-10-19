using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Scales source evidence, never invented content or forced page counts.</summary>
public sealed record ResumeGenerationBudget(int Roles, int AchievementsPerRole, int Projects, int SummaryWords, int ProjectWords)
{
    public static ResumeGenerationBudget Resolve(JobDescription job, ResumeGenerationOptions preferences)
    {
        var structured = job.RequiredSkills.Concat(job.Responsibilities)
            .Concat(job.Competencies.Where(c => c.Priority != RequirementPriority.Preferred).Select(c => c.Name));
        var clauses = ProseJobRequirements.Build(job).Where(item => item.Kind != lucidRESUME.Compiler.RequirementKind.Preferred)
            .Select(item => item.Text);
        var requirements = Math.Max(Count(structured), Count(clauses));
        var growth = Math.Max(0, requirements - 8);
        var budget = preferences.Length switch
        {
            ResumeLength.Concise => new(3, 2, 1, 75, 60),
            ResumeLength.Standard => new(6, 3, 2, 105, 95),
            ResumeLength.Extended => new(12, 6, 5, 160, 140),
            _ => new ResumeGenerationBudget(Math.Min(14, 6 + growth / 3), Math.Min(7, 3 + growth / 6),
                Math.Min(5, 2 + growth / 8), Math.Min(160, 105 + growth * 3), Math.Min(140, 95 + growth * 3))
        };
        return preferences.Detail switch
        {
            ResumeDetail.Brief => budget with { AchievementsPerRole = Math.Min(2, budget.AchievementsPerRole), SummaryWords = Math.Min(80, budget.SummaryWords), ProjectWords = Math.Min(65, budget.ProjectWords) },
            ResumeDetail.Thorough => budget with { AchievementsPerRole = Math.Min(9, budget.AchievementsPerRole + 2), SummaryWords = budget.SummaryWords + 30, ProjectWords = budget.ProjectWords + 40 },
            _ => budget
        };
    }

    private static int Count(IEnumerable<string> values) => values.Select(value => Regex.Replace(value, @"\s+", " ").Trim())
        .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}
