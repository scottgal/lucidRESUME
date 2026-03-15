using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;

namespace lucidRESUME.Matching;

public sealed record ScoredAspect(ExtractedAspect Aspect, int CurrentScore);

/// <summary>
/// Manages aspect voting and builds auto-filters from high-confidence downvotes.
/// </summary>
public sealed class VoteService
{
    private readonly AspectExtractor _extractor;

    public VoteService(AspectExtractor extractor)
    {
        _extractor = extractor;
    }

    /// <summary>Records a +1 vote for an aspect on the given profile.</summary>
    public void VoteUp(UserProfile profile, AspectType type, string value)
        => profile.VoteUp(type, value);

    /// <summary>Records a -1 vote for an aspect on the given profile.</summary>
    public void VoteDown(UserProfile profile, AspectType type, string value)
        => profile.VoteDown(type, value);

    /// <summary>
    /// Builds a hard <see cref="FilterNode"/> from votes with score &lt;= -3.
    /// Returns null if no votes qualify.
    /// </summary>
    public FilterNode? BuildAutoFilters(UserProfile profile)
    {
        // Group all strong downvotes by AspectType
        var disliked = profile.AspectVotes
            .Where(v => v.Score <= -3)
            .GroupBy(v => v.AspectType)
            .ToList();

        if (disliked.Count == 0)
            return null;

        var nodes = new List<FilterNode>();

        foreach (var group in disliked)
        {
            var values = group.Select(v => v.AspectValue).ToArray();

            var fieldName = AspectTypeToField(group.Key);
            if (fieldName is null) continue;

            // For list-based fields (skills) use NotIn on the list;
            // for single-value string fields use NotIn (multi-value) or NotEqual (single)
            var filterNode = values.Length == 1
                ? FilterNode.Leaf(fieldName, FilterOp.NotEqual, values[0])
                : FilterNode.Leaf(fieldName, FilterOp.NotIn, values);

            nodes.Add(filterNode);
        }

        return nodes.Count == 0 ? null : FilterNode.All([.. nodes]);
    }

    /// <summary>
    /// Extracts aspects from a job and annotates them with the user's current vote scores.
    /// </summary>
    public IReadOnlyList<ScoredAspect> GetScoredAspects(JobDescription job, UserProfile profile)
    {
        var aspects = _extractor.Extract(job);
        return aspects
            .Select(a => new ScoredAspect(a, profile.GetVoteScore(a.Type, a.Value)))
            .ToList()
            .AsReadOnly();
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static string? AspectTypeToField(AspectType type) => type switch
    {
        AspectType.Skill         => "skills",
        AspectType.WorkModel     => "work_model",
        AspectType.CompanyType   => "company_type",
        AspectType.Industry      => "industry",
        AspectType.SalaryBand    => null,          // salary handled via numeric filters elsewhere
        AspectType.CultureSignal => null,          // no structured field for culture signals
        _                        => null
    };
}
