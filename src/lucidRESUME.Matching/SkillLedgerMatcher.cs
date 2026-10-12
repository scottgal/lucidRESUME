using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Matches explicit resume skill evidence against JD requirements using audited equivalences.
/// Embeddings retrieve related suggestions only; they never satisfy a requirement.
/// </summary>
public sealed class SkillLedgerMatcher
{
    private readonly IEmbeddingService _embedder;
    public SkillLedgerMatcher(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    public async Task<LedgerMatchResult> MatchAsync(
        SkillLedger resumeLedger, JdSkillLedger jdLedger, CancellationToken ct = default,
        Core.Models.Resume.ResumeDocument? resumeDoc = null)
    {
        var matches = new List<SkillMatch>();
        foreach (var req in jdLedger.Requirements)
        {
            ct.ThrowIfCancellationRequested();
            // A declared/reviewed skill and an audited synonym establish identity.
            // Related terms and semantic proximity can only propose an interview topic.
            var direct = resumeLedger.Entries
                .Where(entry => entry.Evidence.Count > 0 && SkillTaxonomy.AreEquivalent(req.SkillName, entry.SkillName))
                .OrderByDescending(entry => entry.Strength).FirstOrDefault();
            string? suggestion = null;
            float similarity = 0;
            if (direct is null)
            {
                var related = resumeLedger.Entries.FirstOrDefault(entry => entry.Evidence.Count > 0 &&
                    SkillTaxonomy.AreRelated(req.SkillName, entry.SkillName));
                suggestion = related?.SkillName;
                try
                {
                    // Persisted vectors have no guaranteed model identity. Recompute through
                    // the provider cache rather than comparing vectors from different spaces.
                    var query = await _embedder.EmbedAsync(req.SkillName, ct);
                    foreach (var entry in resumeLedger.Entries.Where(entry => entry.Evidence.Count > 0 &&
                                 (related is null || ReferenceEquals(entry, related))))
                    {
                        var vector = await _embedder.EmbedAsync(entry.SkillName, ct);
                        if (query.Length == 0 || query.Length != vector.Length) continue;
                        var score = _embedder.CosineSimilarity(query, vector);
                        if (!float.IsFinite(score) || score < 0 || (related is null && score < 0.6f) || score <= similarity) continue;
                        similarity = score;
                        if (related is null) suggestion = entry.SkillName;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* Retrieval is optional; explicit matches and gaps still work. */ }
            }
            matches.Add(new SkillMatch
            {
                RequiredSkill = req.SkillName,
                Importance = req.Importance,
                MatchedResumeSkill = direct?.SkillName,
                SuggestedResumeSkill = suggestion,
                MatchKind = direct is not null ? SkillMatchKind.Equivalent :
                    suggestion is not null ? SkillMatchKind.Related : SkillMatchKind.Missing,
                Similarity = direct is not null ? 1 : similarity,
                IsMatched = direct is not null,
                EvidenceStrength = direct?.Strength ?? 0,
                CalculatedYears = direct?.CalculatedYears ?? 0,
                EvidenceCount = direct?.Evidence.Count ?? 0,
                RoleCount = direct?.RoleCount ?? 0,
            });
        }

        // Compute overall scores
        var requiredMatches = matches.Where(m => m.Importance == SkillImportance.Required);
        var preferredMatches = matches.Where(m => m.Importance == SkillImportance.Preferred);

        var requiredCoverage = requiredMatches.Any()
            ? requiredMatches.Count(m => m.IsMatched) / (double)requiredMatches.Count()
            : 1.0;
        var preferredCoverage = preferredMatches.Any()
            ? preferredMatches.Count(m => m.IsMatched) / (double)preferredMatches.Count()
            : 0;

        // Overall fit: required coverage weighted 70%, preferred 30%
        var overallFit = requiredCoverage * 0.7 + preferredCoverage * 0.3;

        // Evidence depth: average strength across matched skills
        var avgStrength = matches.Where(m => m.IsMatched)
            .Select(m => m.EvidenceStrength)
            .DefaultIfEmpty(0)
            .Average();

        return new LedgerMatchResult
        {
            OverallFit = overallFit,
            RequiredCoverage = requiredCoverage,
            PreferredCoverage = preferredCoverage,
            AverageEvidenceStrength = avgStrength,
            Matches = matches,
            Gaps = matches.Where(m => !m.IsMatched && m.Importance == SkillImportance.Required)
                .Select(m => m.RequiredSkill).ToList(),
            NearMisses = matches.Where(m => !m.IsMatched && m.SuggestedResumeSkill is not null)
                .Select(m => new NearMiss(m.RequiredSkill, m.SuggestedResumeSkill!, m.Similarity))
                .ToList(),
        };
    }
}

public sealed class LedgerMatchResult
{
    /// <summary>Overall fit score 0-1 (required coverage 70% + preferred coverage 30%).</summary>
    public double OverallFit { get; init; }
    public double RequiredCoverage { get; init; }
    public double PreferredCoverage { get; init; }
    public double AverageEvidenceStrength { get; init; }
    public List<SkillMatch> Matches { get; init; } = [];

    /// <summary>Required skills with no match in the resume.</summary>
    public List<string> Gaps { get; init; } = [];

    /// <summary>Related skills to investigate in an interview; these remain gaps.</summary>
    public List<NearMiss> NearMisses { get; init; } = [];
}

public sealed class SkillMatch
{
    public string RequiredSkill { get; init; } = "";
    public SkillImportance Importance { get; init; }
    public string? MatchedResumeSkill { get; init; }
    public string? SuggestedResumeSkill { get; init; }
    public SkillMatchKind MatchKind { get; init; }
    public float Similarity { get; init; }
    public bool IsMatched { get; init; }
    public double EvidenceStrength { get; init; }
    public double CalculatedYears { get; init; }
    public int EvidenceCount { get; init; }
    public int RoleCount { get; init; }
}

public enum SkillMatchKind { Missing, Equivalent, Related }

public sealed record NearMiss(string RequiredSkill, string ClosestResumeSkill, float Similarity);