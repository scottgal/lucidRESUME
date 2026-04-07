using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Semantic resume compression. Given a JD's skill requirements, queries the
/// skill ledger for evidence of each required skill, selects only the most
/// relevant roles and achievements, and composes a compressed resume.
///
/// A 30-year career with 13 roles becomes 2 pages of laser-targeted evidence.
///
/// The LLM then polishes the pre-filtered content rather than trying to
/// understand and compress the entire career from scratch.
/// </summary>
public sealed class SemanticCompressor
{
    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly JdSkillLedgerBuilder _jdLedgerBuilder;
    private readonly SkillLedgerMatcher _matcher;
    private readonly IEmbeddingService _embedder;
    private readonly ILogger<SemanticCompressor> _logger;

    public SemanticCompressor(
        SkillLedgerBuilder ledgerBuilder,
        JdSkillLedgerBuilder jdLedgerBuilder,
        SkillLedgerMatcher matcher,
        IEmbeddingService embedder,
        ILogger<SemanticCompressor> logger)
    {
        _ledgerBuilder = ledgerBuilder;
        _jdLedgerBuilder = jdLedgerBuilder;
        _matcher = matcher;
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Compress a resume to only the evidence that matches a JD's requirements.
    /// Returns a pre-filtered markdown document ready for LLM polishing.
    /// </summary>
    public async Task<CompressedResume> CompressAsync(
        ResumeDocument resume, JobDescription jd, CancellationToken ct = default)
    {
        var resumeLedger = await _ledgerBuilder.BuildAsync(resume, ct);
        var jdLedger = await _jdLedgerBuilder.BuildAsync(jd, ct);
        var matchResult = await _matcher.MatchAsync(resumeLedger, jdLedger, ct);

        _logger.LogInformation(
            "Compressing resume for {Title} at {Company}: fit={Fit:P0}, {Matched}/{Total} skills matched",
            jd.Title, jd.Company, matchResult.OverallFit,
            matchResult.Matches.Count(m => m.IsMatched), matchResult.Matches.Count);

        // Collect the most relevant experience IDs - roles that evidence required skills
        var relevantRoleIds = new HashSet<Guid>();
        var skillToEvidence = new Dictionary<string, List<SkillEvidence>>();

        foreach (var match in matchResult.Matches.Where(m => m.IsMatched))
        {
            var ledgerEntry = resumeLedger.Find(match.MatchedResumeSkill!);
            if (ledgerEntry is null) continue;

            skillToEvidence[match.RequiredSkill] = ledgerEntry.Evidence
                .OrderByDescending(e => e.Confidence)
                .ThenByDescending(e => e.StartDate)
                .Take(3) // top 3 evidence pieces per skill
                .ToList();

            foreach (var evidence in skillToEvidence[match.RequiredSkill])
            {
                if (evidence.ExperienceId.HasValue)
                    relevantRoleIds.Add(evidence.ExperienceId.Value);
            }
        }

        // Always include current role and most recent 2 roles (even if no direct skill match)
        var recentRoles = resume.Experience
            .OrderByDescending(e => e.StartDate)
            .Take(3)
            .Select(e => e.Id);
        foreach (var id in recentRoles)
            relevantRoleIds.Add(id);

        // Build the compressed markdown
        var md = new StringBuilder();

        // Header
        md.AppendLine($"# {resume.Personal.FullName ?? ""}");
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(resume.Personal.Email)) contactParts.Add(resume.Personal.Email);
        if (!string.IsNullOrEmpty(resume.Personal.Phone)) contactParts.Add(resume.Personal.Phone);
        if (!string.IsNullOrEmpty(resume.Personal.GitHubUrl)) contactParts.Add(resume.Personal.GitHubUrl);
        if (contactParts.Count > 0)
            md.AppendLine(string.Join(" | ", contactParts));
        md.AppendLine();

        // Compressed summary - targeted to the JD
        md.AppendLine("## Summary");
        var matchedSkillNames = matchResult.Matches
            .Where(m => m.IsMatched)
            .Select(m => m.MatchedResumeSkill!)
            .Take(8);
        md.AppendLine($"Experienced professional with demonstrated expertise in {string.Join(", ", matchedSkillNames)}. " +
            (resume.Personal.Summary?.Length > 50 ? resume.Personal.Summary[..Math.Min(200, resume.Personal.Summary.Length)] + "..." : resume.Personal.Summary ?? ""));
        md.AppendLine();

        // Relevant experience only
        md.AppendLine("## Experience");
        md.AppendLine();
        var includedRoles = 0;
        foreach (var exp in resume.Experience.OrderByDescending(e => e.StartDate))
        {
            if (!relevantRoleIds.Contains(exp.Id) && includedRoles >= 3)
                continue; // skip non-relevant roles after we have 3

            md.AppendLine($"### {exp.Title ?? ""} - {exp.Company ?? ""}");
            var dateRange = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
            if (!string.IsNullOrEmpty(dateRange))
                md.AppendLine($"*{dateRange}*");

            // Include only achievements that evidence matched skills
            var relevantAchievements = new List<string>();
            foreach (var achievement in exp.Achievements)
            {
                // Check if this achievement is evidence for any matched skill
                var isEvidence = skillToEvidence.Values
                    .SelectMany(e => e)
                    .Any(e => e.SourceText == achievement);

                if (isEvidence || relevantAchievements.Count < 2) // always include at least 2
                    relevantAchievements.Add(achievement);
            }

            foreach (var a in relevantAchievements.Take(5)) // cap at 5 per role
                md.AppendLine($"- {a}");
            md.AppendLine();
            includedRoles++;
        }

        // Skills section - only matched skills, organized by category
        md.AppendLine("## Skills");
        var skillsByCategory = matchResult.Matches
            .Where(m => m.IsMatched)
            .Select(m => resumeLedger.Find(m.MatchedResumeSkill!))
            .Where(e => e is not null)
            .GroupBy(e => e!.Category ?? "General")
            .ToList();

        foreach (var group in skillsByCategory)
            md.AppendLine($"**{group.Key}:** {string.Join(", ", group.Select(s => s!.SkillName))}");
        md.AppendLine();

        // Education
        if (resume.Education.Count > 0)
        {
            md.AppendLine("## Education");
            foreach (var edu in resume.Education)
                md.AppendLine($"**{edu.Degree ?? ""}** - {edu.Institution ?? ""}");
        }

        var compressedMd = md.ToString();

        return new CompressedResume
        {
            Markdown = compressedMd,
            OriginalRoleCount = resume.Experience.Count,
            IncludedRoleCount = includedRoles,
            OriginalSkillCount = resume.Skills.Count,
            MatchedSkillCount = matchResult.Matches.Count(m => m.IsMatched),
            OverallFit = matchResult.OverallFit,
            Gaps = matchResult.Gaps,
        };
    }

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        if (s == "" && e == "") return "";
        return e == "" ? s : s == "" ? e : $"{s} – {e}";
    }
}

public sealed class CompressedResume
{
    public string Markdown { get; init; } = "";
    public int OriginalRoleCount { get; init; }
    public int IncludedRoleCount { get; init; }
    public int OriginalSkillCount { get; init; }
    public int MatchedSkillCount { get; init; }
    public double OverallFit { get; init; }
    public List<string> Gaps { get; init; } = [];
}