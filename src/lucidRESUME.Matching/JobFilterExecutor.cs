using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

/// <summary>
/// Evaluates a <see cref="FilterNode"/> tree against a <see cref="JobDescription"/>.
/// Adapted from ZenSegment's InMemoryProfileQueryExecutor pattern.
/// </summary>
public sealed class JobFilterExecutor
{
    private static readonly string[] CompanyTypeKeywords =
        ["startup", "start-up", "scale-up", "scaleup", "enterprise", "agency", "consultancy"];

    private static readonly (string[] Keywords, string Industry)[] IndustryKeywords =
    [
        (["fintech", "financial technology"], "Fintech"),
        (["health", "medical", "nhs", "pharma"], "Healthcare"),
        (["defence", "defense", "military", "government"], "Defence"),
        (["gambling", "betting", "casino", "gaming"], "Gambling"),
        (["e-commerce", "ecommerce", "retail"], "E-commerce"),
        (["saas", "software as a service"], "SaaS"),
        (["consulting", "consultancy"], "Consulting"),
        (["agency"], "Agency"),
    ];

    /// <summary>Returns true if the job passes the filter tree.</summary>
    public bool Evaluate(FilterNode filter, JobDescription job) => EvaluateNode(filter, job);

    private static bool EvaluateNode(FilterNode node, JobDescription job)
    {
        if (node.IsLeaf)
            return EvaluateLeaf(node, job);

        return node.Logic switch
        {
            FilterLogic.All => node.Children.All(c => EvaluateNode(c, job)),
            FilterLogic.Any => node.Children.Any(c => EvaluateNode(c, job)),
            FilterLogic.Not => node.Children.Count == 1 && !EvaluateNode(node.Children[0], job),
            _               => false
        };
    }

    private static bool EvaluateLeaf(FilterNode node, JobDescription job)
    {
        if (node.Field is null)
            return false;

        var resolved = ResolveField(node.Field, job);
        return ApplyOp(node, resolved);
    }

    // -------------------------------------------------------------------------
    //  Field resolvers
    // -------------------------------------------------------------------------

    private static object? ResolveField(string field, JobDescription job) =>
        field.ToLowerInvariant() switch
        {
            "skills"        => job.RequiredSkills.Concat(job.PreferredSkills),
            "work_model"    => ResolveWorkModel(job),
            "salary_min"    => job.Salary?.Min,
            "salary_max"    => job.Salary?.Max,
            "company"       => job.Company,
            "company_type"  => ResolveCompanyType(job),
            "location"      => job.Location,
            "title"         => job.Title,
            "industry"      => ResolveIndustry(job),
            _               => null
        };

    private static string ResolveWorkModel(JobDescription job)
    {
        if (job.IsRemote == true)  return "Remote";
        if (job.IsHybrid == true)  return "Hybrid";
        return "Onsite";
    }

    private static string? ResolveCompanyType(JobDescription job)
    {
        var text = (job.RawText ?? "").ToLowerInvariant();
        if (Contains(text, "startup", "start-up", "seed", "series a", "series b")) return "Startup";
        if (Contains(text, "scale-up", "scaleup", "growth stage"))                 return "Scale-up";
        if (Contains(text, "enterprise", "corporate", "ftse", "fortune"))          return "Enterprise";
        if (Contains(text, "agency", "consultancy"))                               return "Agency";
        return null;
    }

    private static string? ResolveIndustry(JobDescription job)
    {
        var text = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();
        foreach (var (keywords, industry) in IndustryKeywords)
        {
            if (Contains(text, keywords))
                return industry;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    //  Operator evaluation
    // -------------------------------------------------------------------------

    private static bool ApplyOp(FilterNode node, object? resolved)
    {
        // Skill list (IEnumerable<string>) — handled separately for In/NotIn
        if (resolved is IEnumerable<string> skillList)
        {
            var skills = skillList.ToList();
            return node.Op switch
            {
                FilterOp.In    => MatchesSkillList(skills, node.Value),
                FilterOp.NotIn => !MatchesSkillList(skills, node.Value),
                _              => false
            };
        }

        // Null handling: NotIn and NotEqual return true for nulls
        if (resolved is null)
        {
            return node.Op switch
            {
                FilterOp.NotIn    => true,
                FilterOp.NotEqual => true,
                _                 => false
            };
        }

        return node.Op switch
        {
            FilterOp.Equal              => StringEqual(resolved, node.Value),
            FilterOp.NotEqual           => !StringEqual(resolved, node.Value),
            FilterOp.Contains           => StringContains(resolved, node.Value),
            FilterOp.In                 => StringIn(resolved, node.Value),
            FilterOp.NotIn              => !StringIn(resolved, node.Value),
            FilterOp.GreaterThan        => NumericCompare(resolved, node.Value) > 0,
            FilterOp.GreaterThanOrEqual => NumericCompare(resolved, node.Value) >= 0,
            FilterOp.LessThan           => NumericCompare(resolved, node.Value) < 0,
            FilterOp.LessThanOrEqual    => NumericCompare(resolved, node.Value) <= 0,
            FilterOp.Between            => NumericCompare(resolved, node.Value) >= 0
                                           && NumericCompare(resolved, node.ValueTo) <= 0,
            FilterOp.IsTrue             => ToDouble(resolved) != 0,
            FilterOp.IsFalse            => ToDouble(resolved) == 0,
            _                           => false
        };
    }

    private static bool MatchesSkillList(IReadOnlyList<string> skills, object? filterValue)
    {
        if (filterValue is null) return false;

        // Support string[] or IEnumerable<string> as the filter value
        IEnumerable<string> candidates = filterValue switch
        {
            string[] arr              => arr,
            IEnumerable<string> seq   => seq,
            string s                  => [s],
            _                         => [filterValue.ToString() ?? ""]
        };

        return candidates.Any(c => skills.Any(s =>
            string.Equals(s, c, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool StringEqual(object resolved, object? filterValue)
    {
        if (filterValue is null) return false;
        return string.Equals(resolved.ToString(), filterValue.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringContains(object resolved, object? filterValue)
    {
        if (filterValue is null) return false;
        return (resolved.ToString() ?? "").Contains(
            filterValue.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringIn(object resolved, object? filterValue)
    {
        if (filterValue is null) return false;

        IEnumerable<string> candidates = filterValue switch
        {
            string[] arr              => arr,
            IEnumerable<string> seq   => seq,
            string s                  => [s],
            _                         => [filterValue.ToString() ?? ""]
        };

        var resolvedStr = resolved.ToString() ?? "";
        return candidates.Any(c => string.Equals(resolvedStr, c, StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    //  Numeric helpers
    // -------------------------------------------------------------------------

    private static int NumericCompare(object left, object? right)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);
        return l.CompareTo(r);
    }

    private static double ToDouble(object? value) => value switch
    {
        double d   => d,
        float f    => f,
        int i      => i,
        long l     => l,
        bool b     => b ? 1 : 0,
        decimal dc => (double)dc,
        string s   => double.TryParse(s, System.Globalization.NumberStyles.Any,
                          System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0,
        _          => 0
    };

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static bool Contains(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
