using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Evidence;
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
    public CompleteCareerRecord? CompleteCareer { get; set; }
    public List<GeneratedResume> GeneratedResumes { get; set; } = [];
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
        ArgumentNullException.ThrowIfNull(resume);
        if (resume.ContentType == CompleteCareerRecord.ContentType)
        {
            CompleteCareer ??= new CompleteCareerRecord();
            CompleteCareer.Transcript = resume;
            CompleteCareer.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }
        var index = Resumes.FindIndex(r => r.ResumeId == resume.ResumeId);
        if (index >= 0)
            Resumes[index] = resume;
        else
            Resumes.Add(resume);

        if (select)
            SelectedResumeId = resume.ResumeId;
    }

    public void SaveReviewedImport(ResumeDocument transcript, ResumeDocument incoming)
    {
        // The merge review has already chosen exactly which additions to accept.
        // Archive the original, and record it as consumed so Save does not import rejected additions.
        var previousLedger = transcript.EvidenceLedger;
        AddOrReplaceResume(incoming);
        if (Resumes.Any(source => source.ResumeId == transcript.ResumeId)) transcript.ResumeId = Guid.NewGuid();
        transcript.ContentType = CompleteCareerRecord.ContentType;
        transcript.FileName = "Complete career transcript";
        transcript.JobMlSource = null;
        transcript.JobMlRevision = null;
        EvidenceLedgerBuilder.RebuildFromSources(transcript,
            [previousLedger, EvidenceLedgerBuilder.EnsureCurrent(incoming)]);
        AddOrReplaceResume(transcript, select: false);
        CompleteCareer!.ImportRevisions[incoming.ResumeId] = Fingerprint(incoming);
        CompleteCareer.OverridesRevision = Fingerprint(Overrides);
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

    /// <summary>Compatibility entry point: reads the saved career, never recomposes imports.</summary>
    public ResumeDocument? BuildAggregateResume() => GetCompleteTranscript();

    public ResumeDocument? GetCompleteTranscript()
    {
        if (CompleteCareer is null) ReconcileCompleteTranscript(); // one-time legacy bootstrap
        return CompleteCareer?.Transcript;
    }

    /// <summary>Called at the persistence boundary, not during matching or generation.
    /// Imported documents are evidence archives. Only changed inputs are composed into the career.</summary>
    public void ReconcileCompleteTranscript()
    {
        NormalizeResumes();
        var record = CompleteCareer;
        var revisions = Resumes.ToDictionary(r => r.ResumeId, Fingerprint);
        var changed = Resumes.Where(r => record is null ||
            !record.ImportRevisions.TryGetValue(r.ResumeId, out var revision) || revision != revisions[r.ResumeId]).ToList();
        var overridesRevision = Fingerprint(Overrides);
        if (record is not null && changed.Count == 0 && record.OverridesRevision == overridesRevision) return;
        if (record is null && Resumes.Count == 0) return;

        // The existing authoritative transcript is retained, including reviewed interviews,
        // edits and review decisions. Import reconciliation is additive; deleting an archive
        // does not silently retract facts from the author's career.
        var warnings = record?.ReviewWarnings.ToList() ?? [];
        var reviewedInputs = changed.Select(source =>
        {
            var snapshot = JsonSerializer.Deserialize<ResumeDocument>(JsonSerializer.Serialize(source))!;
            try { ReviewedEvidenceSnapshot.Apply(snapshot); }
            catch (InvalidDataException ex)
            {
                // Preserve the archive and fail closed on evidence eligibility, not profile loading.
                snapshot.JobMlSource = null;
                foreach (var claim in snapshot.EvidenceLedger.Claims) claim.Review = "required";
                warnings.Add($"{source.FileName}: {ex.Message} Claims need review before matching.");
            }
            return snapshot;
        }).ToList();
        var previous = record is null ? null : JsonSerializer.Deserialize<ResumeDocument>(JsonSerializer.Serialize(record.Transcript))!;
        if (previous is not null)
        {
            // A changed source's old review state must not outvote its newly published decisions.
            var changedIds = changed.Select(source => source.ResumeId).ToHashSet();
            var replacedEvidence = previous.EvidenceLedger.Evidence.Where(item => changedIds.Contains(item.SourceResumeId))
                .Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var claim in previous.EvidenceLedger.Claims)
            {
                if (!claim.EvidenceIds.Any(replacedEvidence.Contains)) continue;
                var remaining = claim.EvidenceIds.Where(id => !replacedEvidence.Contains(id)).ToList();
                if (remaining.Count > 0) claim.EvidenceIds = remaining;
                else
                {
                    var decisions = reviewedInputs.SelectMany(source => source.EvidenceLedger.Claims)
                        .Where(item => item.EvidenceIds.Intersect(claim.EvidenceIds, StringComparer.OrdinalIgnoreCase).Any())
                        .Select(item => item.Review).ToList();
                    claim.Review = decisions.Count > 0 && decisions.All(review => review == "rejected") ? "rejected"
                        : decisions.Any(review => review == "accepted") ? "accepted" : "required";
                }
            }
        }
        var inputs = previous is null ? reviewedInputs : new List<ResumeDocument> { previous };
        if (record is not null) inputs.AddRange(reviewedInputs);
        var composition = new AppState
        {
            Resumes = inputs,
            Overrides = Overrides,
            SelectedResumeId = record?.Transcript.ResumeId ?? SelectedResumeId
        };
        var transcript = composition.ComposeImportedEvidence()!;
        transcript.ResumeId = record?.Transcript.ResumeId ?? Guid.NewGuid();
        transcript.FileName = "Complete career transcript";
        transcript.ContentType = CompleteCareerRecord.ContentType;
        transcript.AggregateEvidenceSources = [];
        transcript.JobMlSource = null; // never inherit a selected import's partial JobML
        transcript.JobMlRevision = null;
        transcript.Experience.RemoveAll(role => !WorkExperienceIdentity.IsValid(role));
        transcript.CanonicalMarkdown = record?.Transcript.CanonicalMarkdown;
        transcript.RawMarkdown = string.Join("\n\n", inputs.SelectMany(source =>
                Regex.Split(source.CanonicalMarkdown ?? source.RawMarkdown ?? "", @"\r?\n\s*\r?\n"))
            .Select(paragraph => paragraph.Trim()).Where(paragraph => paragraph.Length > 0)
            .Distinct(StringComparer.Ordinal));
        transcript.PlainText = transcript.RawMarkdown;
        // Bind claims to the stable career identity while preserving original evidence IDs.
        EvidenceLedgerBuilder.RebuildFromSources(transcript, inputs.Select(EvidenceLedgerBuilder.EnsureCurrent));
        transcript.AggregateRoleIds = new Dictionary<Guid, Guid>();
        if (record is not null)
        {
            var decisions = record.Transcript.EvidenceLedger.Claims.GroupBy(claim => (claim.Kind, claim.SubjectId, claim.Statement))
                .ToDictionary(group => group.Key, group => group.Any(claim => claim.Review == "rejected") ? "rejected" : group.First().Review);
            foreach (var claim in transcript.EvidenceLedger.Claims)
                if (decisions.TryGetValue((claim.Kind, claim.SubjectId, claim.Statement), out var review) && review == "rejected")
                    claim.Review = review;
        }
        CompleteCareer = new CompleteCareerRecord
        {
            Transcript = transcript,
            ImportRevisions = Resumes.ToDictionary(r => r.ResumeId, Fingerprint),
            SemanticIndex = record?.SemanticIndex,
            ReviewWarnings = warnings.Distinct().ToList(),
            OverridesRevision = Fingerprint(Overrides),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private ResumeDocument? ComposeImportedEvidence()
    {
        NormalizeResumes();
        if (Resumes.Count == 0) return null;
        if (Resumes.Count == 1)
        {
            var singleProjection = CloneForAggregate(Resumes[0]);
            ApplyExperienceOverrides(singleProjection.Experience, Overrides);
            ApplyCareerAnchors(singleProjection.Experience, Overrides);
            ApplyPersonalInfoOverrides(singleProjection.Personal, Overrides);
            EvidenceLedgerBuilder.RebuildFromSources(singleProjection, [EvidenceLedgerBuilder.EnsureCurrent(Resumes[0])]);
            return singleProjection;
        }

        var selected = SelectedResume ?? Resumes.Last();
        var aggregate = ResumeDocument.Create("All imported resumes", "application/vnd.lucidresume.aggregate", 0);
        aggregate.ResumeId = selected.ResumeId;
        aggregate.CreatedAt = Resumes.Min(r => r.CreatedAt);
        aggregate.LastModifiedAt = Resumes
            .Select(r => r.LastModifiedAt ?? r.CreatedAt)
            .DefaultIfEmpty(aggregate.CreatedAt)
            .Max();
        aggregate.Personal = CopyPersonalInfo(selected.Personal);
        ApplyPersonalInfoOverrides(aggregate.Personal, Overrides);
        aggregate.CanonicalMarkdown = selected.CanonicalMarkdown;
        aggregate.JobMlSource = selected.JobMlSource;
        aggregate.JobMlRevision = selected.JobMlRevision;
        aggregate.CompleteJobMlUri = selected.CompleteJobMlUri;
        aggregate.IncludeCompactJobMl = selected.IncludeCompactJobMl;
        aggregate.OutputTemplateId = selected.OutputTemplateId;

        aggregate.Experience = DeduplicateExperience(Resumes.SelectMany(r => r.Experience).ToList());
        aggregate.AggregateEvidenceSources = Resumes.ToArray();
        var roleIds = Resumes.SelectMany(source => source.Experience).Select(role => role.Id).Distinct().ToDictionary(id => id);
        foreach (var variant in LastMergeVariants)
            foreach (var original in variant.Variants) roleIds[original.Id] = variant.ExperienceId;
        aggregate.AggregateRoleIds = roleIds;
        ApplyExperienceOverrides(aggregate.Experience, Overrides);
        ApplyCareerAnchors(aggregate.Experience, Overrides);
        aggregate.Education = DeduplicateEducation(Resumes.SelectMany(r => r.Education).ToList());
        aggregate.Certifications = Resumes.SelectMany(r => r.Certifications)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        aggregate.Projects = DeduplicateProjects(Resumes.SelectMany(r => r.Projects).ToList());
        aggregate.Publications = Resumes.SelectMany(resume => resume.Publications)
            .GroupBy(item => item.Url ?? item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        aggregate.Entities = Resumes.SelectMany(r => r.Entities).ToList();
        aggregate.IngestionDecisions = Resumes.SelectMany(r => r.IngestionDecisions).ToList();

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
                    YearsExperience = g.Max(s => s.YearsExperience),
                    EndorsementCount = g.Max(s => s.EndorsementCount),
                    ImportSources = g.SelectMany(s => s.ImportSources)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
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
        EvidenceLedgerBuilder.RebuildFromSources(aggregate, Resumes.Select(EvidenceLedgerBuilder.EnsureCurrent));
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

        var companySimilar = Regex.Replace(ca, @"[^\p{L}\p{N}]", "") == Regex.Replace(cb, @"[^\p{L}\p{N}]", "")
                             || ca.Contains(cb, StringComparison.OrdinalIgnoreCase)
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

    private static readonly string[] CompanySuffixes =
        [" ltd", " limited", " inc", " incorporated", " corp", " corporation",
         " plc", " gmbh", " ab", " llc", " pty", " co", " sa", " ag"];

    /// <summary>
    /// Simple string normalization for backwards-compat dedup.
    /// The primary merge path uses embedding cosine similarity (ResumeDocumentMerger).
    /// </summary>
    private static string NormalizeCompany(string name)
    {
        var lower = name.ToLowerInvariant().Trim().TrimEnd('.');
        foreach (var suffix in CompanySuffixes)
            if (lower.EndsWith(suffix))
                return lower[..^suffix.Length].TrimEnd(',', ' ');
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
        var importSources = primary.ImportSources.Concat(secondary.ImportSources)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new WorkExperience
        {
            Id = a.Id, // The existing canonical role keeps its identity as richer accounts arrive.
            Company = primary.Company?.Length >= (secondary.Company?.Length ?? 0) ? primary.Company : secondary.Company,
            Title = primary.Title?.Length >= (secondary.Title?.Length ?? 0) ? primary.Title : secondary.Title,
            Location = primary.Location ?? secondary.Location,
            StartDate = Min(primary.StartDate, secondary.StartDate),
            EndDate = primary.IsCurrent || secondary.IsCurrent ? null : Max(primary.EndDate, secondary.EndDate),
            IsCurrent = primary.IsCurrent || secondary.IsCurrent,
            IsCareerAnchor = primary.IsCareerAnchor || secondary.IsCareerAnchor,
            Technologies = techs.ToList(),
            Achievements = achievements,
            InterviewEntries = primary.InterviewEntries.Concat(secondary.InterviewEntries).DistinctBy(entry => entry.Id).ToList(),
            ImportSources = importSources,
        };
    }

    private static void ApplyCareerAnchors(IEnumerable<WorkExperience> experience, UserOverrides overrides)
    {
        var companies = overrides.CareerAnchorCompanies
            .Select(NormalizeCompany)
            .Where(company => company.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in experience)
        {
            role.IsCareerAnchor |= overrides.CareerAnchorExperienceIds.Contains(role.Id) ||
                                   overrides.CareerAnchorRoleKeys.Contains(CareerAnchorRoleKey(role)) ||
                                   companies.Contains(NormalizeCompany(role.Company ?? ""));
        }
    }

    private static void ApplyExperienceOverrides(IEnumerable<WorkExperience> experience, UserOverrides overrides)
    {
        foreach (var role in experience)
        {
            var roleKey = CareerAnchorRoleKey(role);
            var correction = overrides.ExperienceOverrides
                .LastOrDefault(candidate => candidate.ExperienceId == role.Id ||
                                            candidate.MatchRoleKey.Equals(roleKey,
                                                StringComparison.OrdinalIgnoreCase));
            if (correction is null) continue;

            role.Company = correction.Company;
            role.Title = correction.Title;
            role.Location = correction.Location;
            role.StartDate = correction.StartDate;
            role.EndDate = correction.IsCurrent ? null : correction.EndDate;
            role.IsCurrent = correction.IsCurrent;
        }
    }

    private static PersonalInfo CopyPersonalInfo(PersonalInfo source) => new()
    {
        FullName = source.FullName,
        Email = source.Email,
        Phone = source.Phone,
        ContactPreference = source.ContactPreference,
        Location = source.Location,
        LinkedInUrl = source.LinkedInUrl,
        GitHubUrl = source.GitHubUrl,
        WebsiteUrl = source.WebsiteUrl,
        Summary = source.Summary
    };

    private static ResumeDocument CloneForAggregate(ResumeDocument source) => new()
    {
        ResumeId = source.ResumeId,
        FileName = source.FileName,
        ContentType = source.ContentType,
        FileSizeBytes = source.FileSizeBytes,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt,
        RawMarkdown = source.RawMarkdown,
        RawJson = source.RawJson,
        PlainText = source.PlainText,
        CanonicalMarkdown = source.CanonicalMarkdown,
        JobMlSource = source.JobMlSource,
        JobMlRevision = source.JobMlRevision,
        CompleteJobMlUri = source.CompleteJobMlUri,
        IncludeCompactJobMl = source.IncludeCompactJobMl,
        OutputTemplateId = source.OutputTemplateId,
        MinimumOutputPages = source.MinimumOutputPages,
        GenerationOptions = source.GenerationOptions,
        TargetRole = source.TargetRole,
        ImageCacheKey = source.ImageCacheKey,
        PageCount = source.PageCount,
        Personal = CopyPersonalInfo(source.Personal),
        Experience = source.Experience.Select(CloneExperience).ToList(),
        Education = [.. source.Education],
        Skills = [.. source.Skills],
        Certifications = [.. source.Certifications],
        Projects = [.. source.Projects],
        Publications = [.. source.Publications],
        RelatedWork = [.. source.RelatedWork],
        ProseIndex = source.ProseIndex,
        Entities = [.. source.Entities],
        IngestionDecisions = [.. source.IngestionDecisions],
        Projection = source.Projection,
        TailoredForJobId = source.TailoredForJobId,
        GenerationEvidenceLinks = [.. source.GenerationEvidenceLinks],
        GenerationWarnings = [.. source.GenerationWarnings]
    };

    private static WorkExperience CloneExperience(WorkExperience source) => new()
    {
        Id = source.Id,
        Company = source.Company,
        Title = source.Title,
        Location = source.Location,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
        IsCurrent = source.IsCurrent,
        IsCompact = source.IsCompact,
        IsCareerAnchor = source.IsCareerAnchor,
        Achievements = [.. source.Achievements],
        InterviewEntries = [.. source.InterviewEntries],
        Technologies = [.. source.Technologies],
        ImportSources = [.. source.ImportSources]
    };

    private static void ApplyPersonalInfoOverrides(PersonalInfo personal, UserOverrides overrides)
    {
        foreach (var (field, value) in overrides.PersonalInfoOverrides)
        {
            switch (field)
            {
                case nameof(PersonalInfo.FullName): personal.FullName = value; break;
                case nameof(PersonalInfo.Email): personal.Email = value; break;
                case nameof(PersonalInfo.Phone): personal.Phone = value; break;
                case nameof(PersonalInfo.ContactPreference): personal.ContactPreference = value; break;
                case nameof(PersonalInfo.Location): personal.Location = value; break;
                case nameof(PersonalInfo.LinkedInUrl):
                case "LinkedIn": personal.LinkedInUrl = value; break;
                case nameof(PersonalInfo.GitHubUrl):
                case "GitHub": personal.GitHubUrl = value; break;
                case nameof(PersonalInfo.WebsiteUrl):
                case "Website": personal.WebsiteUrl = value; break;
                case nameof(PersonalInfo.Summary): personal.Summary = value; break;
            }
        }
    }

    public static string CareerAnchorRoleKey(WorkExperience experience)
    {
        ArgumentNullException.ThrowIfNull(experience);
        return $"{NormalizeCompany(experience.Company ?? "")}|{NormalizeIdentity(experience.Title ?? "")}";
    }

    private static string NormalizeIdentity(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static List<Education> DeduplicateEducation(List<Education> all)
    {
        return all
            .GroupBy(e => NormalizeCompany(e.Institution ?? ""))
            .Select(g =>
            {
                var entries = g.ToList();
                var primary = entries
                    .OrderByDescending(e => (e.Degree?.Length ?? 0) + (e.FieldOfStudy?.Length ?? 0))
                    .First();
                return new Education
                {
                    Id = primary.Id,
                    Institution = primary.Institution,
                    Degree = primary.Degree,
                    FieldOfStudy = primary.FieldOfStudy,
                    StartDate = entries.Select(entry => entry.StartDate).Min(),
                    EndDate = entries.Select(entry => entry.EndDate).Max(),
                    Gpa = primary.Gpa,
                    GraduationYear = primary.GraduationYear,
                    Level = primary.Level,
                    Highlights = entries.SelectMany(e => e.Highlights)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ImportSources = entries.SelectMany(e => e.ImportSources)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            })
            .ToList();
    }

    private static List<Project> DeduplicateProjects(List<Project> all)
    {
        return all.GroupBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToList();
                var primary = entries
                    .OrderByDescending(project => project.Description?.Length ?? 0)
                    .ThenByDescending(project => project.Technologies.Count)
                    .First();
                var metadata = new Dictionary<string, string>(primary.EvidenceMetadata,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                    foreach (var (key, value) in entry.EvidenceMetadata)
                        metadata.TryAdd(key, value);
                return new Project
                {
                    Id = primary.Id,
                    Name = primary.Name,
                    Description = primary.Description,
                    EvidencePassages = entries.SelectMany(project => project.EvidencePassages)
                        .DistinctBy(passage => (passage.Text, passage.SourceUri)).ToList(),
                    Technologies = entries.SelectMany(project => project.Technologies)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ImportSources = entries.SelectMany(project => project.ImportSources)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Url = primary.Url ?? entries.Select(project => project.Url)
                        .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
                    Date = primary.Date ?? entries.Select(project => project.Date).Max(),
                    EvidenceMetadata = metadata
                };
            }).ToList();
    }

    private static DateOnly? Min(DateOnly? a, DateOnly? b) =>
        (a, b) switch { (not null, not null) => a < b ? a : b, (not null, _) => a, _ => b };

    private static DateOnly? Max(DateOnly? a, DateOnly? b) =>
        (a, b) switch { (not null, not null) => a > b ? a : b, (not null, _) => a, _ => b };
}
