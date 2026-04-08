namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Reciprocal rank fusion across extraction signals.
/// Candidates that appear from multiple sources get confidence-boosted.
/// </summary>
public static class JdFieldFuser
{
    public static FusedJdFields Fuse(IReadOnlyList<JdFieldCandidate> candidates, FusionOptions? options = null)
    {
        var opts = options ?? new FusionOptions();
        var result = new FusedJdFields();

        var byType = candidates.GroupBy(c => c.FieldType, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byType)
        {
            var fieldType = group.Key.ToLowerInvariant();

            var fused = group
                .GroupBy(c => Normalize(c.Value))
                .Select(g =>
                {
                    var sources = g.Select(c => c.Source).Distinct().ToList();
                    var maxConfidence = g.Max(c => c.Confidence);
                    var sourceBoost = sources.Count > 1 ? opts.MultiSourceBoost * (sources.Count - 1) : 0;
                    var rrfScore = maxConfidence + sourceBoost;

                    return new FusedCandidate(
                        g.First().Value,
                        Math.Min(1.0, rrfScore),
                        sources);
                })
                .OrderByDescending(f => f.Confidence)
                .ToList();

            switch (fieldType)
            {
                case "title":
                    result.Title = fused.FirstOrDefault();
                    break;
                case "company":
                    result.Company = fused.FirstOrDefault();
                    break;
                case "location":
                    result.Location = fused.FirstOrDefault();
                    break;
                case "remote":
                    result.IsRemote = fused.Any(f => f.Value.Contains("true", StringComparison.OrdinalIgnoreCase)
                                                     || f.Value.Contains("remote", StringComparison.OrdinalIgnoreCase));
                    result.IsHybrid = fused.Any(f => f.Value.Contains("hybrid", StringComparison.OrdinalIgnoreCase));
                    break;
                case "yearsexp":
                    if (int.TryParse(fused.FirstOrDefault()?.Value, out var yrs) && yrs is > 0 and < 50)
                        result.YearsExperience = yrs;
                    break;
                case "salary_min":
                    result.SalaryMin = decimal.TryParse(fused.FirstOrDefault()?.Value, out var sMin) ? sMin : null;
                    break;
                case "salary_max":
                    result.SalaryMax = decimal.TryParse(fused.FirstOrDefault()?.Value, out var sMax) ? sMax : null;
                    break;
                case "salary_currency":
                    result.SalaryCurrency = fused.FirstOrDefault()?.Value;
                    break;
                case "salary_period":
                    result.SalaryPeriod = fused.FirstOrDefault()?.Value;
                    break;
                case "skill":
                    result.Skills = fused;
                    break;
                case "preferredskill":
                    result.PreferredSkills = fused;
                    break;
                case "responsibility":
                    result.Responsibilities = fused;
                    break;
                case "benefit":
                    result.Benefits = fused;
                    break;
                case "education":
                    result.Education = fused.FirstOrDefault();
                    break;
                case "seniority":
                    result.SeniorityLevel = fused.FirstOrDefault();
                    break;
                case "industry":
                    result.Industry = fused.FirstOrDefault();
                    break;
            }
        }

        return result;
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace(".", "").Replace(",", "")
            .Replace("(", "").Replace(")", "");
}

public sealed record FusedCandidate(string Value, double Confidence, List<string> Sources);

public sealed class FusedJdFields
{
    public FusedCandidate? Title { get; set; }
    public FusedCandidate? Company { get; set; }
    public FusedCandidate? Location { get; set; }
    public bool IsRemote { get; set; }
    public bool IsHybrid { get; set; }
    public int? YearsExperience { get; set; }
    public List<FusedCandidate> Skills { get; set; } = [];
    public List<FusedCandidate> PreferredSkills { get; set; } = [];
    public List<FusedCandidate> Responsibilities { get; set; } = [];
    public List<FusedCandidate> Benefits { get; set; } = [];
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryCurrency { get; set; }
    public string? SalaryPeriod { get; set; }
    public FusedCandidate? Education { get; set; }
    public FusedCandidate? SeniorityLevel { get; set; }
    public FusedCandidate? Industry { get; set; }
}
