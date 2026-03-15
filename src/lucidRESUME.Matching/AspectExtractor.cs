using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

public sealed record ExtractedAspect(AspectType Type, string Value, string SourceField);

/// <summary>
/// Extracts votable aspects from a <see cref="JobDescription"/> for display in the UI.
/// </summary>
public sealed class AspectExtractor
{
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

    private static readonly (string[] Keywords, string Signal)[] CultureKeywords =
    [
        (["on-call", "on call", "pagerduty"], "On-call"),
        (["fast-paced", "fast paced", "high-pressure"], "Fast-paced"),
        (["work-life balance", "work life balance"], "Work-life balance"),
        (["flexible hours", "flexible working", "flexibility"], "Flexible hours"),
        (["remote-first", "remote first"], "Remote-first culture"),
        (["no overtime", "sustainable pace"], "Sustainable pace"),
    ];

    public IReadOnlyList<ExtractedAspect> Extract(JobDescription job)
    {
        var results = new List<ExtractedAspect>();
        var seen = new HashSet<(AspectType, string)>(StringComparer.OrdinalIgnoreCase as IEqualityComparer<(AspectType, string)>
                   ?? EqualityComparer<(AspectType, string)>.Default);

        void Add(AspectType type, string value, string source)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!seen.Add((type, value.ToLowerInvariant()))) return;
            results.Add(new ExtractedAspect(type, value, source));
        }

        // Skills
        foreach (var skill in job.RequiredSkills)
            Add(AspectType.Skill, skill, "RequiredSkills");
        foreach (var skill in job.PreferredSkills)
            Add(AspectType.Skill, skill, "PreferredSkills");

        // WorkModel
        string workModel;
        if (job.IsRemote == true)      workModel = "Remote";
        else if (job.IsHybrid == true) workModel = "Hybrid";
        else                           workModel = "Onsite";
        Add(AspectType.WorkModel, workModel, "IsRemote/IsHybrid");

        // SalaryBand
        if (job.Salary?.Min is not null)
        {
            var band = BucketSalary(job.Salary.Min.Value);
            Add(AspectType.SalaryBand, band, "Salary.Min");
        }

        // Industry
        var searchText = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();
        foreach (var (keywords, industry) in IndustryKeywords)
        {
            if (keywords.Any(k => searchText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                Add(AspectType.Industry, industry, "Title/RawText");
                break;
            }
        }

        // CompanyType
        var descText = job.RawText.ToLowerInvariant();
        if (ContainsAny(descText, "startup", "start-up", "seed", "series a", "series b"))
            Add(AspectType.CompanyType, "Startup", "RawText");
        else if (ContainsAny(descText, "scale-up", "scaleup", "growth stage"))
            Add(AspectType.CompanyType, "Scale-up", "RawText");
        else if (ContainsAny(descText, "enterprise", "corporate", "ftse", "fortune"))
            Add(AspectType.CompanyType, "Enterprise", "RawText");
        else if (ContainsAny(descText, "agency", "consultancy"))
            Add(AspectType.CompanyType, "Agency", "RawText");

        // CultureSignals
        foreach (var (keywords, signal) in CultureKeywords)
        {
            if (keywords.Any(k => descText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                Add(AspectType.CultureSignal, signal, "RawText");
        }

        return results.AsReadOnly();
    }

    private static string BucketSalary(decimal min) => min switch
    {
        < 40_000m  => "Under £40k",
        < 60_000m  => "£40-60k",
        < 80_000m  => "£60-80k",
        < 100_000m => "£80-100k",
        _          => "£100k+"
    };

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
