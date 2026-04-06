using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

/// <summary>
/// Detects duplicate/hoover job listings using text embedding similarity.
/// Two JDs posted by different agencies with different titles but
/// identical requirements will be detected as duplicates.
/// Also detects "hoover" roles that recruiters repost continuously.
/// </summary>
public sealed class JobDeduplicator
{
    private readonly IEmbeddingService? _embedder;
    private const float DuplicateThreshold = 0.92f; // very high = near-identical
    private const float SimilarThreshold = 0.80f;   // high = same role, different wording

    public JobDeduplicator(IEmbeddingService? embedder = null)
    {
        _embedder = embedder;
    }

    /// <summary>
    /// Find potential duplicates for a new job among existing jobs.
    /// Returns matches sorted by similarity.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
        JobDescription newJob, IReadOnlyList<JobDescription> existingJobs, CancellationToken ct = default)
    {
        if (_embedder is null || existingJobs.Count == 0) return [];

        var newText = BuildComparisonText(newJob);
        if (newText.Length < 50) return [];

        var newEmb = await _embedder.EmbedAsync(newText, ct);
        var matches = new List<DuplicateMatch>();

        foreach (var existing in existingJobs)
        {
            if (existing.JobId == newJob.JobId) continue;

            var existingText = BuildComparisonText(existing);
            if (existingText.Length < 50) continue;

            var existingEmb = await _embedder.EmbedAsync(existingText, ct);
            var similarity = _embedder.CosineSimilarity(newEmb, existingEmb);

            if (similarity >= SimilarThreshold)
            {
                matches.Add(new DuplicateMatch
                {
                    ExistingJob = existing,
                    Similarity = similarity,
                    IsDuplicate = similarity >= DuplicateThreshold,
                    Reason = similarity >= DuplicateThreshold
                        ? "Near-identical listing (possibly reposted or different agency)"
                        : "Very similar role — may be same position with different wording",
                });
            }
        }

        return matches.OrderByDescending(m => m.Similarity).ToList();
    }

    /// <summary>
    /// Detect "hoover" patterns — companies that repost the same role repeatedly.
    /// </summary>
    public List<HooverDetection> DetectHooverRoles(IReadOnlyList<JobDescription> jobs)
    {
        // Group by company + similar title
        var byCompany = jobs
            .Where(j => !string.IsNullOrEmpty(j.Company))
            .GroupBy(j => j.Company!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .ToList();

        var hooverRoles = new List<HooverDetection>();

        foreach (var company in byCompany)
        {
            // Check if same company posted similar roles multiple times
            var sorted = company.OrderByDescending(j => j.CreatedAt).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var titleSim = TitleSimilarity(sorted[i].Title, sorted[j].Title);
                    if (titleSim > 0.7)
                    {
                        var daysBetween = (sorted[i].CreatedAt - sorted[j].CreatedAt).TotalDays;
                        hooverRoles.Add(new HooverDetection
                        {
                            Company = company.Key,
                            Title = sorted[i].Title ?? "?",
                            PostCount = company.Count(),
                            DaysBetweenPosts = (int)daysBetween,
                            IsLikelyHoover = daysBetween < 60 && company.Count() >= 3,
                        });
                        break; // one detection per company pair
                    }
                }
            }
        }

        return hooverRoles;
    }

    private static string BuildComparisonText(JobDescription job)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(job.Title)) parts.Add(job.Title);
        if (job.RequiredSkills.Count > 0) parts.Add(string.Join(", ", job.RequiredSkills));
        if (!string.IsNullOrEmpty(job.RawText) && job.RawText.Length > 100)
            parts.Add(job.RawText[..Math.Min(500, job.RawText.Length)]);
        return string.Join(" | ", parts);
    }

    private static double TitleSimilarity(string? a, string? b)
    {
        if (a is null || b is null) return 0;
        var wordsA = a.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (wordsA.Count == 0 || wordsB.Count == 0) return 0;
        var intersection = wordsA.Intersect(wordsB).Count();
        return (double)intersection / Math.Max(wordsA.Count, wordsB.Count);
    }
}

public sealed class DuplicateMatch
{
    public JobDescription ExistingJob { get; init; } = null!;
    public float Similarity { get; init; }
    public bool IsDuplicate { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class HooverDetection
{
    public string Company { get; init; } = "";
    public string Title { get; init; } = "";
    public int PostCount { get; init; }
    public int DaysBetweenPosts { get; init; }
    public bool IsLikelyHoover { get; init; }
}
