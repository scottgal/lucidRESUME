using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Matching;

/// <summary>
/// Provides skill centroid matching from a preloaded taxonomy (5,630 skills across 16 roles).
/// Used by JD parser to find skills embedded in prose and by the UI for instant archetype matching.
///
/// Architecture:
/// 1. Skills loaded from Resources/taxonomies/skill-taxonomy.txt (role|skill format)
/// 2. Embeddings computed lazily on first use
/// 3. FindSkillsInText scans text chunks against all centroids using cosine similarity
/// 4. GetArchetype returns how well a set of skills matches each role profile
/// </summary>
public sealed class SkillTaxonomyService : ISkillTaxonomy
{
    private readonly IEmbeddingService _embedder;
    private readonly Lazy<TaxonomyData> _data;

    public SkillTaxonomyService(IEmbeddingService embedder)
    {
        _embedder = embedder;
        _data = new Lazy<TaxonomyData>(LoadTaxonomy);
    }

    /// <summary>All unique skill names in the taxonomy.</summary>
    public IReadOnlySet<string> AllSkills => _data.Value.AllSkills;

    /// <summary>All role names (e.g. "Backend Developer", "DevOps Engineer").</summary>
    public IReadOnlyList<string> Roles => _data.Value.Roles;

    /// <summary>Skills for a specific role archetype.</summary>
    public IReadOnlySet<string> GetRoleSkills(string role) =>
        _data.Value.RoleSkills.TryGetValue(role, out var skills) ? skills : new HashSet<string>();

    /// <summary>
    /// Finds known skills in text using exact match against the taxonomy.
    /// Fast — no embeddings needed. Handles case-insensitive matching.
    /// Returns distinct skill names found in the text.
    /// </summary>
    public List<string> FindSkillsExact(string text)
    {
        var lower = text.ToLowerInvariant();
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in _data.Value.AllSkills)
        {
            if (skill.Length < 2) continue;

            // Exact word boundary match (avoid "C" matching "Company")
            var skillLower = skill.ToLowerInvariant();
            var idx = lower.IndexOf(skillLower, StringComparison.Ordinal);
            while (idx >= 0)
            {
                // Check word boundaries
                var before = idx > 0 ? lower[idx - 1] : ' ';
                var after = idx + skillLower.Length < lower.Length ? lower[idx + skillLower.Length] : ' ';

                var isWordBoundary = !char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after);
                // For single/two char skills (C#, C++, R, Go), require non-letter boundary
                if (skillLower.Length <= 2)
                    isWordBoundary = isWordBoundary && (before == ' ' || before == ',' || before == '\n' || idx == 0);

                if (isWordBoundary && seen.Add(skill))
                {
                    found.Add(skill);
                    break;
                }

                idx = lower.IndexOf(skillLower, idx + 1, StringComparison.Ordinal);
            }
        }

        return found;
    }

    /// <summary>
    /// Computes how well a set of skills matches each role archetype.
    /// Returns roles sorted by match percentage (descending).
    /// </summary>
    public List<(string Role, double MatchPercent, int Matched, int Total)> GetArchetypeMatches(
        IReadOnlyCollection<string> candidateSkills)
    {
        var candidateSet = new HashSet<string>(candidateSkills, StringComparer.OrdinalIgnoreCase);
        var results = new List<(string Role, double MatchPercent, int Matched, int Total)>();

        foreach (var (role, roleSkills) in _data.Value.RoleSkills)
        {
            if (roleSkills.Count == 0) continue;
            var matched = roleSkills.Count(s => candidateSet.Contains(s));
            var pct = (double)matched / roleSkills.Count;
            results.Add((role, pct, matched, roleSkills.Count));
        }

        return results.OrderByDescending(r => r.MatchPercent).ToList();
    }

    /// <summary>
    /// Finds skills using embedding similarity against taxonomy centroids.
    /// Slower than exact match but catches semantic variations.
    /// </summary>
    public async Task<List<(string Skill, double Similarity)>> FindSkillsSemantic(
        string text, float threshold = 0.78f, CancellationToken ct = default)
    {
        await EnsureEmbeddingsAsync(ct);

        var textEmbedding = await _embedder.EmbedAsync(text, ct);
        var results = new List<(string Skill, double Similarity)>();

        foreach (var (skill, embedding) in _data.Value.SkillEmbeddings)
        {
            var sim = _embedder.CosineSimilarity(textEmbedding, embedding);
            if (sim >= threshold)
                results.Add((skill, sim));
        }

        return results.OrderByDescending(r => r.Similarity).ToList();
    }

    /// <summary>
    /// Scans text line by line and finds skills using both exact and semantic matching.
    /// Returns JD field candidates with confidence scores for RRF fusion.
    /// </summary>
    public async Task<List<(string Skill, double Confidence)>> FindSkillsInText(
        string text, CancellationToken ct = default)
    {
        var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Signal 1: Exact match (fast, high confidence)
        foreach (var skill in FindSkillsExact(text))
        {
            results[skill] = 0.85;
        }

        // Signal 2: Semantic match on chunks (slower, catches variations)
        // Split text into manageable chunks for embedding
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 10 || line.Length > 200) continue;

            var semantic = await FindSkillsSemantic(line, 0.82f, ct);
            foreach (var (skill, sim) in semantic.Take(5)) // Cap per-line to avoid noise
            {
                if (results.TryGetValue(skill, out var existing))
                    results[skill] = Math.Min(1.0, existing + 0.10); // Multi-source boost
                else
                    results[skill] = sim * 0.8; // Discount semantic-only slightly
            }
        }

        return results.Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(r => r.Value)
            .ToList();
    }

    private async Task EnsureEmbeddingsAsync(CancellationToken ct)
    {
        var data = _data.Value;
        if (data.SkillEmbeddings.Count > 0) return;

        // Batch-embed all skills
        foreach (var skill in data.AllSkills)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var embedding = await _embedder.EmbedAsync(skill, ct);
                data.SkillEmbeddings[skill] = embedding;
            }
            catch { /* skip failed embeddings */ }
        }
    }

    private static TaxonomyData LoadTaxonomy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "taxonomies", "skill-taxonomy.txt");
        if (!File.Exists(path))
        {
            var asmDir = Path.GetDirectoryName(typeof(SkillTaxonomyService).Assembly.Location)!;
            path = Path.Combine(asmDir, "Resources", "taxonomies", "skill-taxonomy.txt");
        }

        var allSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleSkills = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var pipeIdx = line.IndexOf('|');
                if (pipeIdx <= 0) continue;

                var role = line[..pipeIdx].Trim();
                var skill = line[(pipeIdx + 1)..].Trim();
                if (skill.Length < 2) continue;

                allSkills.Add(skill);
                if (!roleSkills.TryGetValue(role, out var set))
                    roleSkills[role] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(skill);
            }
        }

        return new TaxonomyData
        {
            AllSkills = allSkills,
            Roles = roleSkills.Keys.OrderBy(r => r).ToList(),
            RoleSkills = roleSkills,
        };
    }

    private sealed class TaxonomyData
    {
        public HashSet<string> AllSkills { get; init; } = [];
        public List<string> Roles { get; init; } = [];
        public Dictionary<string, HashSet<string>> RoleSkills { get; init; } = new();
        public Dictionary<string, float[]> SkillEmbeddings { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
