using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Indexes resume content into the VectorStore on import.
/// Embeds skills, achievement bullets, and job titles for KNN search.
/// This enables: "find similar skills", "find jobs matching this bullet",
/// "which of my achievements best evidences this JD requirement".
/// </summary>
public sealed class EmbeddingIndexer
{
    private readonly IEmbeddingService _embedder;
    private readonly ILogger<EmbeddingIndexer> _logger;

    public EmbeddingIndexer(IEmbeddingService embedder, ILogger<EmbeddingIndexer> logger)
    {
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Index all indexable content from a resume into the VectorStore.
    /// Call after successful import/parse.
    /// </summary>
    public async Task IndexResumeAsync(ResumeDocument resume, VectorStore vectors, CancellationToken ct = default)
    {
        var indexed = 0;
        var nextId = await vectors.NextRowIdAsync(ct);

        // Index skills
        foreach (var skill in resume.Skills)
        {
            try
            {
                var emb = await _embedder.EmbedAsync(skill.Name, ct);
                await vectors.UpsertAsync(nextId++, emb, "skill", skill.Name,
                    $"{skill.Category}: {skill.Name}", ct);
                indexed++;
            }
            catch { /* individual embedding failure non-fatal */ }
        }

        // Index achievement bullets (most valuable for matching)
        foreach (var exp in resume.Experience)
        {
            foreach (var achievement in exp.Achievements)
            {
                if (achievement.Length < 20) continue;
                try
                {
                    var emb = await _embedder.EmbedAsync(achievement, ct);
                    await vectors.UpsertAsync(nextId++, emb, "achievement",
                        exp.Id.ToString(),
                        achievement, ct);
                    indexed++;
                }
                catch { /* non-fatal */ }
            }
        }

        // Index job titles
        foreach (var exp in resume.Experience)
        {
            var title = $"{exp.Title} at {exp.Company}";
            if (string.IsNullOrWhiteSpace(exp.Title)) continue;
            try
            {
                var emb = await _embedder.EmbedAsync(title, ct);
                await vectors.UpsertAsync(nextId++, emb, "jobtitle",
                    exp.Id.ToString(), title, ct);
                indexed++;
            }
            catch { /* non-fatal */ }
        }

        _logger.LogInformation("Indexed {Count} vectors from resume", indexed);
    }

    /// <summary>
    /// Find the K achievements most similar to a query (e.g., a JD requirement).
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> FindSimilarAchievementsAsync(
        string query, VectorStore vectors, int k = 5, CancellationToken ct = default)
    {
        var emb = await _embedder.EmbedAsync(query, ct);
        return await vectors.SearchAsync(emb, k, sourceTypeFilter: "achievement", ct: ct);
    }

    /// <summary>
    /// Find the K skills most similar to a query skill name.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> FindSimilarSkillsAsync(
        string query, VectorStore vectors, int k = 5, CancellationToken ct = default)
    {
        var emb = await _embedder.EmbedAsync(query, ct);
        return await vectors.SearchAsync(emb, k, sourceTypeFilter: "skill", ct: ct);
    }
}
