using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Tracking;
using System.Text.Json.Serialization;

namespace lucidRESUME.Core.Persistence;

/// Single-user local store.
public interface IAppStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);

    /// <summary>
    /// Atomically loads, applies <paramref name="mutate"/>, and saves state
    /// under a single lock - prevents concurrent callers from overwriting each other.
    /// </summary>
    Task MutateAsync(Action<AppState> mutate, CancellationToken ct = default);

    /// <summary>Export full app state as JSON to a stream.</summary>
    Task ExportJsonAsync(Stream output, CancellationToken ct = default);

    /// <summary>Import app state from a JSON stream, replacing current state.</summary>
    Task ImportJsonAsync(Stream input, CancellationToken ct = default);
}

public sealed class AppState
{
    public List<ResumeDocument> Resumes { get; set; } = [];
    public Guid? SelectedResumeId { get; set; }

    public UserProfile Profile { get; set; } = new();
    public List<JobDescription> Jobs { get; set; } = [];
    public DateTimeOffset LastSaved { get; set; }
    public List<SavedSearch> SavedSearches { get; set; } = [];
    public List<SearchPreset> CustomPresets { get; set; } = [];
    public List<JobApplication> Applications { get; set; } = [];
    public List<SearchWatch> SearchWatches { get; set; } = [];
    public UserOverrides Overrides { get; set; } = new();
    public EmployerProfile? EmployerProfile { get; set; }

    // Returns built-ins + custom presets merged
    [JsonIgnore]
    public IReadOnlyList<SearchPreset> AllPresets =>
        [.. SearchPreset.BuiltIns, .. CustomPresets];

    [JsonIgnore]
    public ResumeDocument? SelectedResume =>
        SelectedResumeId is { } id
            ? Resumes.FirstOrDefault(r => r.ResumeId == id) ?? Resumes.LastOrDefault()
            : Resumes.LastOrDefault();

    public void AddOrReplaceResume(ResumeDocument resume, bool select = true)
    {
        var index = Resumes.FindIndex(r => r.ResumeId == resume.ResumeId);
        if (index >= 0)
            Resumes[index] = resume;
        else
            Resumes.Add(resume);

        if (select)
            SelectedResumeId = resume.ResumeId;
    }

    public void NormalizeResumes()
    {
        Resumes = Resumes
            .Where(r => r.ResumeId != Guid.Empty)
            .GroupBy(r => r.ResumeId)
            .Select(g => g.Last())
            .OrderBy(r => r.CreatedAt)
            .ToList();

        if (Resumes.Count == 0)
        {
            SelectedResumeId = null;
            return;
        }

        if (SelectedResumeId is null || Resumes.All(r => r.ResumeId != SelectedResumeId))
            SelectedResumeId = Resumes.Last().ResumeId;
    }

    public ResumeDocument? BuildAggregateResume()
    {
        NormalizeResumes();
        if (Resumes.Count == 0) return null;
        if (Resumes.Count == 1) return Resumes[0];

        var selected = SelectedResume ?? Resumes.Last();
        var aggregate = ResumeDocument.Create("All imported resumes", "application/vnd.lucidresume.aggregate", 0);
        aggregate.ResumeId = selected.ResumeId;
        aggregate.CreatedAt = Resumes.Min(r => r.CreatedAt);
        aggregate.LastModifiedAt = Resumes
            .Select(r => r.LastModifiedAt ?? r.CreatedAt)
            .DefaultIfEmpty(aggregate.CreatedAt)
            .Max();
        aggregate.Personal = selected.Personal;

        aggregate.Experience = DeduplicateExperience(Resumes.SelectMany(r => r.Experience).ToList());
        aggregate.Education = DeduplicateEducation(Resumes.SelectMany(r => r.Education).ToList());
        aggregate.Certifications = Resumes.SelectMany(r => r.Certifications)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        aggregate.Projects = Resumes.SelectMany(r => r.Projects)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Technologies.Count).First())
            .ToList();
        aggregate.Entities = Resumes.SelectMany(r => r.Entities).ToList();

        aggregate.Skills = Resumes
            .SelectMany(r => r.Skills)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new Skill
                {
                    Name = first.Name,
                    Category = g.Select(s => s.Category).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                    YearsExperience = g.Max(s => s.YearsExperience)
                };
            })
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Store merge variants for user review (only new ones not already stored)
        var existingIds = Overrides.ExperienceVariants.Select(v => v.ExperienceId).ToHashSet();
        foreach (var variant in LastMergeVariants.Where(v => !existingIds.Contains(v.ExperienceId)))
            Overrides.ExperienceVariants.Add(variant);

        aggregate.PlainText = string.Join("\n\n", Resumes.Select(r => r.PlainText).Where(s => !string.IsNullOrWhiteSpace(s)));
        aggregate.RawMarkdown = string.Join("\n\n---\n\n", Resumes.Select(r => r.RawMarkdown).Where(s => !string.IsNullOrWhiteSpace(s)));
        return aggregate;
    }

    /// <summary>
    /// Deduplicates work experience entries across resume variants.
    /// Matches by company name similarity + date range overlap.
    /// Different wordings for the same role (e.g. "Lead Dev" vs "Lead Developer")
    /// are merged, keeping the richer entry (more achievements/technologies).
    /// </summary>
    /// <summary>Variants found during the last deduplication, for user review.</summary>
    internal List<ExperienceVariant> LastMergeVariants { get; } = [];

    private List<WorkExperience> DeduplicateExperience(List<WorkExperience> all)
    {
        if (all.Count <= 1) return all;

        LastMergeVariants.Clear();
        var merged = new List<WorkExperience>();
        var used = new HashSet<int>();

        for (var i = 0; i < all.Count; i++)
        {
            if (used.Contains(i)) continue;

            var variants = new List<WorkExperience> { all[i] };
            var best = all[i];

            for (var j = i + 1; j < all.Count; j++)
            {
                if (used.Contains(j)) continue;
                if (!AreOverlapping(best, all[j])) continue;

                variants.Add(all[j]);
                best = MergeExperience(best, all[j]);
                used.Add(j);
            }

            // Store variants when there were conflicts (different wordings for same role)
            if (variants.Count > 1)
            {
                LastMergeVariants.Add(new ExperienceVariant
                {
                    ExperienceId = best.Id,
                    Variants = variants,
                });
            }

            merged.Add(best);
        }

        return merged.OrderByDescending(e => e.StartDate).ToList();
    }

    private static bool AreOverlapping(WorkExperience a, WorkExperience b)
    {
        // Company name must be similar (fuzzy: one contains the other, or starts the same)
        var ca = NormalizeCompany(a.Company ?? "");
        var cb = NormalizeCompany(b.Company ?? "");
        if (ca.Length == 0 || cb.Length == 0) return false;

        var companySimilar = ca.Contains(cb, StringComparison.OrdinalIgnoreCase)
                             || cb.Contains(ca, StringComparison.OrdinalIgnoreCase)
                             || ca.Equals(cb, StringComparison.OrdinalIgnoreCase);
        if (!companySimilar) return false;

        // Date ranges must overlap or be within 3 months
        if (a.StartDate is null || b.StartDate is null) return companySimilar;
        var aStart = a.StartDate.Value.DayNumber;
        var aEnd = (a.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : a.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
        var bStart = b.StartDate.Value.DayNumber;
        var bEnd = (b.IsCurrent ? DateOnly.FromDateTime(DateTime.Today) : b.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;

        const int graceDays = 90; // 3 months
        return aStart <= bEnd + graceDays && bStart <= aEnd + graceDays;
    }

    private static string NormalizeCompany(string name)
    {
        // Strip common suffixes: Ltd, Limited, Inc, Corp, Plc, GmbH, AB, etc.
        var suffixes = new[] { " ltd", " limited", " inc", " corp", " plc", " gmbh", " ab", " llc", " pty" };
        var lower = name.ToLowerInvariant().Trim().TrimEnd('.');
        foreach (var suffix in suffixes)
            if (lower.EndsWith(suffix))
                lower = lower[..^suffix.Length].TrimEnd(',', ' ');
        return lower;
    }

    private static WorkExperience MergeExperience(WorkExperience a, WorkExperience b)
    {
        // Keep the one with more achievements; merge technologies
        var primary = a.Achievements.Count >= b.Achievements.Count ? a : b;
        var secondary = primary == a ? b : a;

        // Merge technologies
        var techs = new HashSet<string>(primary.Technologies, StringComparer.OrdinalIgnoreCase);
        foreach (var t in secondary.Technologies) techs.Add(t);

        // Merge achievements (add unique ones from secondary)
        var achievements = new List<string>(primary.Achievements);
        foreach (var ach in secondary.Achievements)
        {
            if (!achievements.Any(a2 => a2.Contains(ach, StringComparison.OrdinalIgnoreCase)
                                        || ach.Contains(a2, StringComparison.OrdinalIgnoreCase)))
                achievements.Add(ach);
        }

        return new WorkExperience
        {
            Id = primary.Id,
            Company = primary.Company?.Length >= (secondary.Company?.Length ?? 0) ? primary.Company : secondary.Company,
            Title = primary.Title?.Length >= (secondary.Title?.Length ?? 0) ? primary.Title : secondary.Title,
            Location = primary.Location ?? secondary.Location,
            StartDate = Min(primary.StartDate, secondary.StartDate),
            EndDate = primary.IsCurrent || secondary.IsCurrent ? null : Max(primary.EndDate, secondary.EndDate),
            IsCurrent = primary.IsCurrent || secondary.IsCurrent,
            Technologies = techs.ToList(),
            Achievements = achievements,
        };
    }

    private static List<Education> DeduplicateEducation(List<Education> all)
    {
        return all
            .GroupBy(e => NormalizeCompany(e.Institution ?? ""))
            .Select(g => g.OrderByDescending(e => (e.Degree?.Length ?? 0) + (e.FieldOfStudy?.Length ?? 0)).First())
            .ToList();
    }

    private static DateOnly? Min(DateOnly? a, DateOnly? b) =>
        (a, b) switch { (not null, not null) => a < b ? a : b, (not null, _) => a, _ => b };

    private static DateOnly? Max(DateOnly? a, DateOnly? b) =>
        (a, b) switch { (not null, not null) => a > b ? a : b, (not null, _) => a, _ => b };
}
