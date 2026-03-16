using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Quality;

namespace lucidRESUME.Matching;

/// <summary>
/// Assesses the quality and completeness of a job description.
/// Useful as an internal signal: a well-defined JD produces better matching and tailoring.
/// Also surfaced to the user as feedback when a JD is sparse.
/// </summary>
public sealed class JobQualityAnalyser : IJobQualityAnalyser
{
    public QualityReport Analyse(JobDescription job)
    {
        var completeness   = CheckCompleteness(job);
        var clarity        = CheckClarity(job);
        var skillsQuality  = CheckSkills(job);

        int compScore  = ScoreFromFindings(completeness, 7);
        int clarScore  = ScoreFromFindings(clarity, 4);
        int skillScore = ScoreFromFindings(skillsQuality, Math.Max(job.RequiredSkills.Count, 3));

        var categories = new List<QualityCategory>
        {
            new("Completeness",    compScore,  50, completeness),
            new("Clarity",         clarScore,  30, clarity),
            new("Skills Detail",   skillScore, 20, skillsQuality),
        };

        int overall = categories.Sum(c => c.Score * c.Weight) / categories.Sum(c => c.Weight);
        return new QualityReport(overall, categories, DateTimeOffset.UtcNow);
    }

    // ── Completeness ──────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckCompleteness(JobDescription job)
    {
        var findings = new List<QualityFinding>();

        if (string.IsNullOrWhiteSpace(job.Title))
            findings.Add(new("Core", FindingSeverity.Error, "NO_TITLE", "Job title missing"));

        if (string.IsNullOrWhiteSpace(job.Company))
            findings.Add(new("Core", FindingSeverity.Warning, "NO_COMPANY", "Company name missing"));

        if (string.IsNullOrWhiteSpace(job.Location) && job.IsRemote != true)
            findings.Add(new("Core", FindingSeverity.Warning, "NO_LOCATION",
                "Location not specified and not marked as remote"));

        if (job.Salary is null)
            findings.Add(new("Core", FindingSeverity.Info, "NO_SALARY",
                "Salary range not found — adds uncertainty to application"));

        if (job.RequiredSkills.Count == 0)
            findings.Add(new("Skills", FindingSeverity.Error, "NO_REQUIRED_SKILLS",
                "No required skills extracted — matching accuracy will be low"));

        if (job.Responsibilities.Count == 0)
            findings.Add(new("Content", FindingSeverity.Warning, "NO_RESPONSIBILITIES",
                "No responsibilities section found"));

        if (string.IsNullOrWhiteSpace(job.RawText) || job.RawText.Length < 200)
            findings.Add(new("Content", FindingSeverity.Warning, "SPARSE_TEXT",
                $"JD text is very short ({job.RawText.Length} chars) — likely incomplete"));

        return findings;
    }

    // ── Clarity ──────────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckClarity(JobDescription job)
    {
        var findings = new List<QualityFinding>();

        // Too many required skills is a red flag (spray-and-pray JD)
        if (job.RequiredSkills.Count > 20)
            findings.Add(new("Skills", FindingSeverity.Warning, "TOO_MANY_REQUIRED",
                $"{job.RequiredSkills.Count} required skills is unusually high — may be a boilerplate or unrealistic JD"));

        // Years of experience
        if (job.RequiredYearsExperience is null)
            findings.Add(new("Requirements", FindingSeverity.Info, "NO_YEARS_EXP",
                "Years of experience not specified"));
        else if (job.RequiredYearsExperience > 10)
            findings.Add(new("Requirements", FindingSeverity.Warning, "VERY_HIGH_EXP",
                $"Requires {job.RequiredYearsExperience}+ years — may be unrealistic or mislabelled seniority"));

        // Education requirements
        if (string.IsNullOrWhiteSpace(job.RequiredEducation))
            findings.Add(new("Requirements", FindingSeverity.Info, "NO_EDUCATION_REQ",
                "Education requirements not specified"));

        // Vague title detection
        if (!string.IsNullOrWhiteSpace(job.Title))
        {
            var vagueTerms = new[] { "various", "multiple", "team member", "staff", "associate" };
            if (vagueTerms.Any(v => job.Title.Contains(v, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("Core", FindingSeverity.Info, "VAGUE_TITLE",
                    $"Job title \"{job.Title}\" is vague — consider if this is the right role"));
        }

        return findings;
    }

    // ── Skills quality ────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckSkills(JobDescription job)
    {
        var findings = new List<QualityFinding>();

        // Detect very short (likely unparsed) skill entries
        var tooShort = job.RequiredSkills.Where(s => s.Length < 2).ToList();
        if (tooShort.Count > 0)
            findings.Add(new("Skills", FindingSeverity.Warning, "SHORT_SKILLS",
                $"{tooShort.Count} required skill(s) are very short (parsing artifact?)"));

        // Duplicate skills
        var dupes = job.RequiredSkills
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
            findings.Add(new("Skills", FindingSeverity.Info, "DUPLICATE_SKILLS",
                $"Duplicate required skills: {string.Join(", ", dupes)}"));

        return findings;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int ScoreFromFindings(IReadOnlyList<QualityFinding> findings, int opportunities)
    {
        if (opportunities <= 0) return 100;
        int errorCount   = findings.Count(f => f.Severity == FindingSeverity.Error);
        int warningCount = findings.Count(f => f.Severity == FindingSeverity.Warning);
        double penalty   = (errorCount * 2.0 + warningCount) / (opportunities * 2.0);
        return Math.Clamp((int)((1.0 - penalty) * 100), 0, 100);
    }
}
