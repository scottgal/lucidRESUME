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

        aggregate.Experience = Resumes.SelectMany(r => r.Experience).ToList();
        aggregate.Education = Resumes.SelectMany(r => r.Education).ToList();
        aggregate.Certifications = Resumes.SelectMany(r => r.Certifications).ToList();
        aggregate.Projects = Resumes.SelectMany(r => r.Projects).ToList();
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

        aggregate.PlainText = string.Join("\n\n", Resumes.Select(r => r.PlainText).Where(s => !string.IsNullOrWhiteSpace(s)));
        aggregate.RawMarkdown = string.Join("\n\n---\n\n", Resumes.Select(r => r.RawMarkdown).Where(s => !string.IsNullOrWhiteSpace(s)));
        return aggregate;
    }
}
