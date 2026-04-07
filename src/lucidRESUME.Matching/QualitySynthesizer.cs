using lucidRESUME.Core.Models.Quality;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Synthesizes raw quality findings + skill ledger into grouped, actionable suggestions.
/// Replaces the massive raw findings list with a concise improvement plan.
/// </summary>
public sealed class QualitySynthesizer
{
    /// <summary>
    /// Synthesize raw quality findings into grouped, prioritized suggestions.
    /// </summary>
    public QualitySynthesis Synthesize(QualityReport rawReport, SkillLedger? ledger = null)
    {
        var synthesis = new QualitySynthesis
        {
            OverallScore = rawReport.OverallScore,
        };

        // Group findings by broad category (map codes to groups)
        var grouped = new Dictionary<string, List<QualityFinding>>();
        foreach (var f in rawReport.AllFindings)
        {
            var cat = ClassifyFindingCategory(f.Code, f.Section);
            if (!grouped.TryGetValue(cat, out var list))
                grouped[cat] = list = [];
            list.Add(f);
        }

        foreach (var (category, findings) in grouped)
        {
            var errorCount = findings.Count(f => f.Severity == FindingSeverity.Error);
            var warningCount = findings.Count(f => f.Severity == FindingSeverity.Warning);

            // Skip categories with no actual issues
            if (errorCount + warningCount == 0 && findings.All(f => f.Severity == FindingSeverity.Info))
                continue;

            var suggestion = category switch
            {
                "Achievements" => SynthesizeBulletAdvice(findings),
                "Sections" => SynthesizeSectionAdvice(findings),
                "Formatting" => SynthesizeFormatAdvice(findings),
                "Contact" => SynthesizeContactAdvice(findings),
                "Length" => SynthesizeLengthAdvice(findings),
                _ => new QualitySuggestion
                {
                    Category = category,
                    Title = $"{errorCount + warningCount} {category.ToLowerInvariant()} issues",
                    Summary = string.Join("; ", findings.Take(3).Select(f => f.Message)),
                    Severity = errorCount > 0 ? SuggestionSeverity.Important : SuggestionSeverity.Minor,
                    AffectedCount = findings.Count,
                }
            };

            // Don't show suggestions with 0 affected items
            if (suggestion.AffectedCount > 0)
                synthesis.Suggestions.Add(suggestion);
        }

        // Add skill ledger insights if available
        if (ledger is not null)
        {
            AddLedgerInsights(synthesis, ledger);
        }

        // Sort: important first, then by affected count
        synthesis.Suggestions = synthesis.Suggestions
            .OrderByDescending(s => s.Severity)
            .ThenByDescending(s => s.AffectedCount)
            .ToList();

        return synthesis;
    }

    private static string ClassifyFindingCategory(string code, string? section)
    {
        var lower = code.ToLowerInvariant();
        if (lower.Contains("bullet") || lower.Contains("metric") || lower.Contains("verb") ||
            lower.Contains("achievement") || lower.Contains("quantity"))
            return "Achievements";
        if (lower.Contains("section") || lower.Contains("missing"))
            return "Sections";
        if (lower.Contains("format") || lower.Contains("length") || lower.Contains("long") ||
            lower.Contains("short"))
            return section?.Contains("format", StringComparison.OrdinalIgnoreCase) == true ? "Formatting" : "Length";
        if (lower.Contains("contact") || lower.Contains("email") || lower.Contains("phone"))
            return "Contact";
        if (lower.Contains("grad") || lower.Contains("edu"))
            return "Education";
        return "Other";
    }

    private static QualitySuggestion SynthesizeBulletAdvice(List<QualityFinding> findings)
    {
        var noMetric = findings.Count(f => f.Code.Contains("METRIC") || f.Message.Contains("metric"));
        var weakVerb = findings.Count(f => f.Code.Contains("VERB") || f.Message.Contains("action verb"));
        var tooLong = findings.Count(f => f.Code.Contains("LONG") || f.Message.Contains("long"));

        var parts = new List<string>();
        if (noMetric > 0) parts.Add($"add metrics to {noMetric} bullet(s)");
        if (weakVerb > 0) parts.Add($"strengthen {weakVerb} weak opening verb(s)");
        if (tooLong > 0) parts.Add($"shorten {tooLong} overly long bullet(s)");

        return new QualitySuggestion
        {
            Category = "Achievements",
            Title = noMetric > findings.Count / 2
                ? "Most bullets lack measurable results"
                : $"{findings.Count} bullet improvements available",
            Summary = parts.Count > 0
                ? string.Join(", ", parts)
                : "Review achievement bullets for specificity",
            Severity = noMetric > 5 ? SuggestionSeverity.Important : SuggestionSeverity.Moderate,
            AffectedCount = findings.Count,
        };
    }

    private static QualitySuggestion SynthesizeSectionAdvice(List<QualityFinding> findings)
    {
        var missing = findings.Where(f => f.Message.Contains("missing")).Select(f => f.Section).ToList();

        return new QualitySuggestion
        {
            Category = "Sections",
            Title = missing.Count > 0 ? $"Missing sections: {string.Join(", ", missing)}" : "Section improvements",
            Summary = string.Join("; ", findings.Take(3).Select(f => f.Message)),
            Severity = missing.Count > 0 ? SuggestionSeverity.Important : SuggestionSeverity.Minor,
            AffectedCount = findings.Count,
        };
    }

    private static QualitySuggestion SynthesizeFormatAdvice(List<QualityFinding> findings)
    {
        return new QualitySuggestion
        {
            Category = "Formatting",
            Title = $"{findings.Count} formatting suggestions",
            Summary = string.Join("; ", findings.Take(3).Select(f => f.Message)),
            Severity = SuggestionSeverity.Minor,
            AffectedCount = findings.Count,
        };
    }

    private static QualitySuggestion SynthesizeContactAdvice(List<QualityFinding> findings)
    {
        return new QualitySuggestion
        {
            Category = "Contact Info",
            Title = "Contact information gaps",
            Summary = string.Join("; ", findings.Select(f => f.Message)),
            Severity = findings.Any(f => f.Severity == FindingSeverity.Error)
                ? SuggestionSeverity.Important : SuggestionSeverity.Moderate,
            AffectedCount = findings.Count,
        };
    }

    private static QualitySuggestion SynthesizeLengthAdvice(List<QualityFinding> findings)
    {
        return new QualitySuggestion
        {
            Category = "Length",
            Title = findings.First().Message,
            Summary = "Optimal resume length is 1-2 pages for most roles",
            Severity = SuggestionSeverity.Moderate,
            AffectedCount = 1,
        };
    }

    private static void AddLedgerInsights(QualitySynthesis synthesis, SkillLedger ledger)
    {
        // Unsubstantiated skills
        var unsubstantiated = ledger.UnsubstantiatedSkills;
        if (unsubstantiated.Count > 0)
        {
            synthesis.Suggestions.Add(new QualitySuggestion
            {
                Category = "Skills Evidence",
                Title = $"{unsubstantiated.Count} skills listed without experience evidence",
                Summary = $"Add specific achievements for: {string.Join(", ", unsubstantiated.Take(5).Select(s => s.SkillName))}",
                Severity = unsubstantiated.Count > 5 ? SuggestionSeverity.Important : SuggestionSeverity.Moderate,
                AffectedCount = unsubstantiated.Count,
            });
        }

        // Consistency issues from the ledger
        foreach (var issue in ledger.Issues.Where(i => i.Severity == ConsistencySeverity.Error))
        {
            synthesis.Suggestions.Add(new QualitySuggestion
            {
                Category = "Consistency",
                Title = issue.Description,
                Summary = $"Review {issue.SkillName} - evidence doesn't match claims",
                Severity = SuggestionSeverity.Important,
                AffectedCount = 1,
            });
        }

        // Strong skills worth highlighting
        var topSkills = ledger.StrongSkills.Take(3).ToList();
        if (topSkills.Count > 0)
        {
            synthesis.Strengths.Add(
                $"Strongest skills: {string.Join(", ", topSkills.Select(s => $"{s.SkillName} ({s.CalculatedYears:F0}y, {s.RoleCount} roles)"))}");
        }
    }
}

public sealed class QualitySynthesis
{
    public int OverallScore { get; set; }
    public List<QualitySuggestion> Suggestions { get; set; } = [];
    public List<string> Strengths { get; set; } = [];
}

public sealed class QualitySuggestion
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public SuggestionSeverity Severity { get; set; }
    public int AffectedCount { get; set; }
}

public enum SuggestionSeverity
{
    Minor,
    Moderate,
    Important,
}