using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Matches a resume skill ledger against a JD skill ledger using multi-vector similarity.
/// Each skill is embedded, then we compute how well the resume's evidence covers the JD's requirements.
/// </summary>
public sealed class SkillLedgerMatcher
{
    private readonly IEmbeddingService _embedder;
    private const float SemanticMatchThreshold = 0.58f; // catches "NoSQL" ↔ "MongoDB" (0.59), "AI/ML" ↔ "ML.NET"

    // Stop words loaded from Resources/stopwords.txt
    private static readonly Lazy<HashSet<string>> StopWords = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "stopwords.txt");
        if (!File.Exists(path))
            path = Path.Combine(Path.GetDirectoryName(typeof(SkillLedgerMatcher).Assembly.Location)!, "Resources", "stopwords.txt");
        if (File.Exists(path))
        {
            return new HashSet<string>(
                File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                    .Select(l => l.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
        // Fallback if file not found
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "with", "and", "the", "for", "from", "that", "this", "have", "been", "experience", "strong" };
    });

    public SkillLedgerMatcher(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    public async Task<LedgerMatchResult> MatchAsync(
        SkillLedger resumeLedger, JdSkillLedger jdLedger, CancellationToken ct = default,
        Core.Models.Resume.ResumeDocument? resumeDoc = null)
    {
        // Embed all resume skills
        var resumeEmbeddings = new Dictionary<string, float[]>();
        foreach (var entry in resumeLedger.Entries)
        {
            var emb = await _embedder.EmbedAsync(entry.SkillName, ct);
            resumeEmbeddings[entry.SkillName] = emb;
        }

        // Embed all JD requirements
        foreach (var req in jdLedger.Requirements)
        {
            req.Embedding ??= await _embedder.EmbedAsync(req.SkillName, ct);
        }

        // Match each JD requirement to the best resume skill
        // JD ledger now provides clean skill names (not full sentences)
        var matches = new List<SkillMatch>();
        foreach (var req in jdLedger.Requirements)
        {
            SkillLedgerEntry? bestEntry = null;
            float bestSim = 0;
            string? bestResumeName = null;
            bool substringMatch = false;

            var reqLower = req.SkillName.ToLowerInvariant();

            foreach (var (name, emb) in resumeEmbeddings)
            {
                var nameLower = name.ToLowerInvariant();

                // Fast path: substring match (works well now that JD terms are clean)
                // "C#" == "C#", "Azure" in "Microsoft Azure", "Docker" == "Docker"
                if (reqLower.Contains(nameLower) || nameLower.Contains(reqLower))
                {
                    var entry = resumeLedger.Find(name);
                    if (entry is not null && (bestEntry is null || !substringMatch || entry.Strength > bestEntry.Strength))
                    {
                        bestSim = Math.Max(bestSim, 0.85f);
                        bestResumeName = name;
                        bestEntry = entry;
                        substringMatch = true;
                    }
                    continue;
                }

                // Taxonomy path: "k8s" matches "kubernetes", "AWS" matches "amazon web services"
                if (!substringMatch && SkillTaxonomy.AreEquivalent(reqLower, nameLower))
                {
                    var entry = resumeLedger.Find(name);
                    if (entry is not null)
                    {
                        bestSim = Math.Max(bestSim, 0.82f);
                        bestResumeName = name;
                        bestEntry = entry;
                        substringMatch = true; // treat taxonomy match as definitive
                        continue;
                    }
                }

                // Semantic path: embedding similarity
                if (req.Embedding is not null)
                {
                    var sim = _embedder.CosineSimilarity(req.Embedding, emb);
                    if (sim > bestSim && !substringMatch)
                    {
                        bestSim = sim;
                        bestResumeName = name;
                        bestEntry = resumeLedger.Find(name);
                    }
                }
            }

            // Fallback: search raw resume experience achievements for JD requirement words
            // "payment systems" → found in "Stripe Connect and Hyperwallet payment systems"
            if (!substringMatch && bestSim < SemanticMatchThreshold && resumeDoc is not null)
            {
                var reqWords = reqLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3 && !StopWords.Value.Contains(w))
                    .ToList();

                foreach (var exp in resumeDoc.Experience)
                {
                    foreach (var ach in exp.Achievements)
                    {
                        var achLower = ach.ToLowerInvariant();
                        var hits = reqWords.Count(w => achLower.Contains(w));

                        if (hits >= 2 || (reqWords.Count == 1 && hits >= 1))
                        {
                            var snippet = ach.Length > 60 ? ach[..57] + "..." : ach;
                            bestSim = 0.65f;
                            bestResumeName = $"(evidence: {snippet})";
                            // Find or create a ledger entry for context
                            bestEntry = resumeLedger.Entries.FirstOrDefault() ?? new SkillLedgerEntry { SkillName = "?" };
                            substringMatch = true;
                            break;
                        }
                    }
                    if (substringMatch) break;
                }
            }

            var isMatch = (substringMatch || bestSim >= SemanticMatchThreshold) && bestEntry is not null;

            matches.Add(new SkillMatch
            {
                RequiredSkill = req.SkillName,
                Importance = req.Importance,
                MatchedResumeSkill = isMatch ? bestResumeName : null,
                Similarity = bestSim,
                IsMatched = isMatch,
                EvidenceStrength = isMatch ? bestEntry!.Strength : 0,
                CalculatedYears = isMatch ? bestEntry!.CalculatedYears : 0,
                EvidenceCount = isMatch ? bestEntry!.Evidence.Count : 0,
                RoleCount = isMatch ? bestEntry!.RoleCount : 0,
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
            NearMisses = matches.Where(m => !m.IsMatched && m.Similarity >= 0.6f)
                .Select(m => new NearMiss(m.RequiredSkill, m.MatchedResumeSkill ?? "", m.Similarity))
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

    /// <summary>Skills that are close but below the match threshold - potential bridge skills.</summary>
    public List<NearMiss> NearMisses { get; init; } = [];
}

public sealed class SkillMatch
{
    public string RequiredSkill { get; init; } = "";
    public SkillImportance Importance { get; init; }
    public string? MatchedResumeSkill { get; init; }
    public float Similarity { get; init; }
    public bool IsMatched { get; init; }
    public double EvidenceStrength { get; init; }
    public double CalculatedYears { get; init; }
    public int EvidenceCount { get; init; }
    public int RoleCount { get; init; }
}

public sealed record NearMiss(string RequiredSkill, string ClosestResumeSkill, float Similarity);