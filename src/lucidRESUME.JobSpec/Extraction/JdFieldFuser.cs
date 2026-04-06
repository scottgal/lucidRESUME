namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Reciprocal rank fusion across extraction signals.
/// Candidates that appear from multiple sources get confidence-boosted.
/// </summary>
public static class JdFieldFuser
{
    /// <summary>
    /// Fuses candidates per field type using configurable weights.
    /// </summary>
    public static FusedJdFields Fuse(IReadOnlyList<JdFieldCandidate> candidates, FusionOptions? options = null)
    {
        var opts = options ?? new FusionOptions();
        var result = new FusedJdFields();

        var byType = candidates.GroupBy(c => c.FieldType, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byType)
        {
            var fieldType = group.Key.ToLowerInvariant();

            // Group by normalized value, fuse confidence across sources
            var fused = group
                .GroupBy(c => Normalize(c.Value))
                .Select(g =>
                {
                    var sources = g.Select(c => c.Source).Distinct().ToList();
                    var maxConfidence = g.Max(c => c.Confidence);
                    var sourceBoost = sources.Count > 1 ? opts.MultiSourceBoost * (sources.Count - 1) : 0;
                    var rrfScore = maxConfidence + sourceBoost;

                    return new FusedCandidate(
                        g.First().Value, // original casing from highest-confidence source
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
                    break;
                case "yearsexp":
                    if (int.TryParse(fused.FirstOrDefault()?.Value, out var yrs))
                        result.YearsExperience = yrs;
                    break;
                case "salary_min":
                    result.SalaryMin = decimal.TryParse(fused.FirstOrDefault()?.Value, out var sMin) ? sMin : null;
                    break;
                case "salary_max":
                    result.SalaryMax = decimal.TryParse(fused.FirstOrDefault()?.Value, out var sMax) ? sMax : null;
                    break;
                case "skill":
                    result.Skills = fused;
                    break;
                case "preferredskill":
                    result.PreferredSkills = fused;
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
    public int? YearsExperience { get; set; }
    public List<FusedCandidate> Skills { get; set; } = [];
    public List<FusedCandidate> PreferredSkills { get; set; } = [];
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
}
