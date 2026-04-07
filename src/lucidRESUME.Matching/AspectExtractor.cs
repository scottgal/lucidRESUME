using System.Collections.Concurrent;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

public sealed record ExtractedAspect(AspectType Type, string Value, string SourceField);

/// <summary>
/// Extracts votable aspects from a <see cref="JobDescription"/> for display in the UI.
/// Results are memoized per <see cref="JobDescription.JobId"/> - repeated calls for the
/// same job (e.g. from SkillMatchingService and VoteService in a single search pass) return
/// the cached list without re-scanning the raw text.
/// </summary>
public sealed class AspectExtractor
{
    private readonly CompanyClassifier _classifier;
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<ExtractedAspect>> _cache = new();

    public AspectExtractor(CompanyClassifier classifier)
    {
        _classifier = classifier;
    }

    public IReadOnlyList<ExtractedAspect> Extract(JobDescription job)
        => _cache.GetOrAdd(job.JobId, _ => ExtractCore(job));

    private IReadOnlyList<ExtractedAspect> ExtractCore(JobDescription job)
    {
        var results = new List<ExtractedAspect>();
        // Use a (type, normalised-value) set for deduplication; value is already lowercased before Add
        var seen = new HashSet<(AspectType, string)>();

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
            Add(AspectType.SalaryBand, BucketSalary(job.Salary.Min.Value), "Salary.Min");

        // Industry - collect ALL matching industries (a job can be both Fintech and SaaS)
        var searchText = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();
        foreach (var (keywords, industry) in JobKeywords.Industries)
        {
            if (keywords.Any(k => searchText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                Add(AspectType.Industry, industry, "Title/RawText");
        }

        // CompanyType - use persisted value if already classified, otherwise classify now
        var companyType = job.CompanyType != CompanyType.Unknown
            ? job.CompanyType
            : _classifier.Classify(job);

        if (companyType != CompanyType.Unknown)
            Add(AspectType.CompanyType, companyType.ToString(), "CompanyType");

        // CultureSignals - all that match
        var descText = job.RawText.ToLowerInvariant();
        foreach (var (keywords, signal) in JobKeywords.CultureSignals)
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
}