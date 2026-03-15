using lucidRESUME.Core.Models.Filters;

namespace lucidRESUME.Core.Models.Profile;

public sealed class UserProfile
{
    public Guid ProfileId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    // Who they are
    public string? DisplayName { get; set; }
    public string? CurrentTitle { get; set; }
    public int? YearsOfExperience { get; set; }

    // What they want
    public WorkPreferences Preferences { get; set; } = new();

    // Skills they actively want to use
    public List<SkillPreference> SkillsToEmphasise { get; set; } = [];

    // Skills they have but want to avoid
    public List<SkillPreference> SkillsToAvoid { get; set; } = [];

    // Companies/orgs they won't work for
    public List<string> BlockedCompanies { get; set; } = [];

    // Free-form notes for the AI (context, career goals, anything)
    public string? CareerGoals { get; set; }
    public string? AdditionalContext { get; set; }

    public void BlockCompany(string company)
    {
        if (!BlockedCompanies.Contains(company, StringComparer.OrdinalIgnoreCase))
            BlockedCompanies.Add(company);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EmphasiseSkill(string skill, string? reason = null)
    {
        SkillsToEmphasise.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AvoidSkill(string skill, string? reason = null)
    {
        SkillsToAvoid.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // -------------------------------------------------------------------------
    //  Aspect voting
    // -------------------------------------------------------------------------

    public List<AspectVote> AspectVotes { get; private set; } = [];

    /// <summary>
    /// O(1) lookup cache built lazily from <see cref="AspectVotes"/>.
    /// Invalidated on every mutation via <see cref="InvalidateCache"/>.
    /// </summary>
    private Dictionary<(AspectType, string), AspectVote>? _voteCache;

    public void VoteUp(AspectType type, string value)
    {
        var vote = GetOrCreateVote(type, value);
        vote.Score = Math.Min(5, vote.Score + 1);
        vote.LastVoted = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        InvalidateCache();
    }

    public void VoteDown(AspectType type, string value)
    {
        var vote = GetOrCreateVote(type, value);
        vote.Score = Math.Max(-5, vote.Score - 1);
        vote.LastVoted = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        InvalidateCache();
    }

    /// <summary>O(1) lookup after the first call — cache is built on demand.</summary>
    public int GetVoteScore(AspectType type, string value)
    {
        _voteCache ??= BuildCache();
        var key = (type, value.ToLowerInvariant());
        return _voteCache.TryGetValue(key, out var vote) ? vote.Score : 0;
    }

    private Dictionary<(AspectType, string), AspectVote> BuildCache()
        => AspectVotes.ToDictionary(
            v => (v.AspectType, v.AspectValue.ToLowerInvariant()),
            v => v);

    private void InvalidateCache() => _voteCache = null;

    private AspectVote GetOrCreateVote(AspectType type, string value)
    {
        // Work against the list directly; cache is rebuilt after mutation
        var normalised = value.ToLowerInvariant();
        var existing = AspectVotes.FirstOrDefault(v =>
            v.AspectType == type &&
            string.Equals(v.AspectValue, value, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var newVote = new AspectVote { AspectType = type, AspectValue = value };
        AspectVotes.Add(newVote);
        return newVote;
    }
}
