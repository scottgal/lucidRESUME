using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.Core.Persistence;
using lucidRESUME.EmailTracker;

namespace lucidRESUME.ViewModels.Pages;

public sealed record ApplicationCardViewModel(
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    string Company,
    ApplicationStage Stage,
    string StageBadge,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? LastActivity,
    int DaysSinceActivity,
    bool IsStale,
    int TimelineEventCount,
    JobApplication FullApplication);

public sealed record TimelineEventViewModel(
    string Title,
    string? Description,
    string Timestamp,
    TimelineEventType Type,
    bool IsAutoDetected);

public sealed record FunnelStageData(
    ApplicationStage Stage,
    string Label,
    int Count,
    double Percentage);

public sealed partial class PipelinePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly EmailScanOrchestrator _emailScanner;
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(14);

    [ObservableProperty] private ObservableCollection<ApplicationCardViewModel> _applications = [];
    [ObservableProperty] private ApplicationCardViewModel? _selectedApplication;
    [ObservableProperty] private ObservableCollection<TimelineEventViewModel> _selectedTimeline = [];
    [ObservableProperty] private ObservableCollection<FunnelStageData> _funnelData = [];

    // Stats
    [ObservableProperty] private int _totalApplications;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _interviewCount;
    [ObservableProperty] private int _offerCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private string _responseRate = "0%";
    [ObservableProperty] private string _interviewRate = "0%";
    [ObservableProperty] private string _offerRate = "0%";

    // Detail panel
    [ObservableProperty] private string? _selectedNoteText;
    [ObservableProperty] private string? _recruiterName;
    [ObservableProperty] private string? _recruiterEmail;
    [ObservableProperty] private string? _statusMessage;

    // Stage filter (null = show all)
    [ObservableProperty] private ApplicationStage? _stageFilter;

    // Email scan
    [ObservableProperty] private bool _isEmailConfigured;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _scanResult;

    public PipelinePageViewModel(IAppStore store, EmailScanOrchestrator emailScanner)
    {
        _store = store;
        _emailScanner = emailScanner;
        IsEmailConfigured = emailScanner.IsConfigured;
    }

    public async Task LoadAsync()
    {
        var state = await _store.LoadAsync();

        // Auto-detect ghosted: Applied for 30+ days with no activity
        var ghostedCandidates = state.Applications
            .Where(a => a.Stage == ApplicationStage.Applied &&
                        (DateTimeOffset.UtcNow - (a.LastActivityAt ?? a.CreatedAt)).TotalDays >= 30)
            .ToList();

        if (ghostedCandidates.Count > 0)
        {
            await _store.MutateAsync(s =>
            {
                foreach (var ghost in ghostedCandidates)
                {
                    var app = s.Applications.FirstOrDefault(a => a.ApplicationId == ghost.ApplicationId);
                    if (app is not null && app.Stage == ApplicationStage.Applied)
                        app.AdvanceTo(ApplicationStage.Ghosted);
                }
            });
            state = await _store.LoadAsync();
            StatusMessage = $"{ghostedCandidates.Count} application(s) marked as Ghosted (30+ days no response)";
        }

        var apps = state.Applications
            .OrderByDescending(a => a.LastActivityAt ?? a.CreatedAt)
            .ToList();

        var cards = apps.Select(ToCard).ToList();

        Applications = new ObservableCollection<ApplicationCardViewModel>(
            StageFilter is null ? cards : cards.Where(c => c.Stage == StageFilter));

        UpdateStats(apps);
        UpdateFunnel(apps);
    }

    [RelayCommand]
    private void SelectApplication(ApplicationCardViewModel? app)
    {
        SelectedApplication = app;
    }

    partial void OnStageFilterChanged(ApplicationStage? value)
    {
        // Re-filter without reloading from store
        _ = LoadAsync();
    }

    partial void OnSelectedApplicationChanged(ApplicationCardViewModel? value)
    {
        if (value is null)
        {
            SelectedTimeline = [];
            RecruiterName = null;
            RecruiterEmail = null;
            return;
        }

        var app = value.FullApplication;
        RecruiterName = app.Contact.RecruiterName;
        RecruiterEmail = app.Contact.RecruiterEmail;

        SelectedTimeline = new ObservableCollection<TimelineEventViewModel>(
            app.Timeline
                .OrderByDescending(e => e.Timestamp)
                .Select(e => new TimelineEventViewModel(
                    e.Title,
                    e.Description,
                    FormatTimestamp(e.Timestamp),
                    e.Type,
                    e.IsAutoDetected)));
    }

    [RelayCommand]
    private async Task AdvanceStage()
    {
        if (SelectedApplication is null) return;
        var next = GetNextStage(SelectedApplication.Stage);
        if (next is null) return;

        await _store.MutateAsync(state =>
        {
            var app = state.Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication.ApplicationId);
            app?.AdvanceTo(next.Value);
        });

        await LoadAsync();
        // Re-select the same application
        SelectedApplication = Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication?.ApplicationId);
    }

    [RelayCommand]
    private async Task SetStage(string stageName)
    {
        if (SelectedApplication is null) return;
        if (!Enum.TryParse<ApplicationStage>(stageName, out var stage)) return;

        await _store.MutateAsync(state =>
        {
            var app = state.Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication.ApplicationId);
            app?.AdvanceTo(stage);
        });

        await LoadAsync();
        SelectedApplication = Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication?.ApplicationId);
    }

    [RelayCommand]
    private async Task AddNote()
    {
        if (SelectedApplication is null || string.IsNullOrWhiteSpace(SelectedNoteText)) return;
        var noteText = SelectedNoteText;
        SelectedNoteText = null;

        await _store.MutateAsync(state =>
        {
            var app = state.Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication.ApplicationId);
            app?.AddNote(noteText);
        });

        await LoadAsync();
        SelectedApplication = Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication?.ApplicationId);
    }

    [RelayCommand]
    private async Task SaveContact()
    {
        if (SelectedApplication is null) return;
        var name = RecruiterName;
        var email = RecruiterEmail;

        await _store.MutateAsync(state =>
        {
            var app = state.Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication.ApplicationId);
            if (app is null) return;
            app.Contact.RecruiterName = name;
            app.Contact.RecruiterEmail = email;
        });

        StatusMessage = "Contact saved";
    }

    [RelayCommand]
    private async Task RemoveApplication()
    {
        if (SelectedApplication is null) return;
        var id = SelectedApplication.ApplicationId;

        await _store.MutateAsync(state =>
        {
            state.Applications.RemoveAll(a => a.ApplicationId == id);
        });

        SelectedApplication = null;
        await LoadAsync();
    }

    [RelayCommand]
    private void FilterByStage(string? stageName)
    {
        StageFilter = stageName is not null && Enum.TryParse<ApplicationStage>(stageName, out var s) ? s : null;
    }

    [RelayCommand]
    private async Task ScanEmail()
    {
        if (!_emailScanner.IsConfigured)
        {
            ScanResult = "Email not configured. Set IMAP credentials in appsettings.json or user secrets.";
            return;
        }

        IsScanning = true;
        ScanResult = "Scanning...";
        try
        {
            var summary = await _emailScanner.ScanAsync();
            ScanResult = $"Scanned {summary.EmailsScanned} emails: " +
                         $"{summary.JobRelatedFound} job-related, {summary.Matched} matched, " +
                         $"{summary.TimelineEventsCreated} events, {summary.StageAdvances} stage advances";

            if (summary.UnmatchedEmails.Count > 0)
                ScanResult += $"\n{summary.UnmatchedEmails.Count} unmatched: {string.Join(", ", summary.UnmatchedEmails.Take(3))}";

            await LoadAsync();
            if (SelectedApplication is not null)
                SelectedApplication = Applications.FirstOrDefault(a => a.ApplicationId == SelectedApplication.ApplicationId);
        }
        catch (Exception ex)
        {
            ScanResult = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Creates a new application from a JobDescription and navigates here.</summary>
    public async Task TrackJobAsync(Guid jobId)
    {
        var state = await _store.LoadAsync();
        // Don't double-track
        if (state.Applications.Any(a => a.JobId == jobId)) return;
        var job = state.Jobs.FirstOrDefault(j => j.JobId == jobId);
        if (job is null) return;

        await _store.MutateAsync(s =>
        {
            s.Applications.Add(JobApplication.Create(job));
        });

        await LoadAsync();
    }

    /// <summary>Marks a tracked job as Applied (creates application if needed).</summary>
    public async Task MarkAsAppliedAsync(Guid jobId)
    {
        await _store.MutateAsync(state =>
        {
            var app = state.Applications.FirstOrDefault(a => a.JobId == jobId);
            if (app is null)
            {
                var job = state.Jobs.FirstOrDefault(j => j.JobId == jobId);
                if (job is null) return;
                app = JobApplication.Create(job);
                state.Applications.Add(app);
            }
            if (app.Stage == ApplicationStage.Saved)
                app.AdvanceTo(ApplicationStage.Applied);
        });
    }

    private ApplicationCardViewModel ToCard(JobApplication app)
    {
        var daysSince = app.LastActivityAt.HasValue
            ? (int)(DateTimeOffset.UtcNow - app.LastActivityAt.Value).TotalDays
            : (int)(DateTimeOffset.UtcNow - app.CreatedAt).TotalDays;

        var isTerminal = app.Stage is ApplicationStage.Accepted or ApplicationStage.Rejected
            or ApplicationStage.Withdrawn or ApplicationStage.Ghosted;

        return new ApplicationCardViewModel(
            app.ApplicationId,
            app.JobId,
            app.JobTitle ?? "Untitled",
            app.CompanyName ?? "Unknown",
            app.Stage,
            app.Stage.ToString(),
            app.AppliedAt,
            app.LastActivityAt,
            daysSince,
            IsStale: !isTerminal && daysSince >= StaleThreshold.Days,
            app.Timeline.Count,
            app);
    }

    private void UpdateStats(List<JobApplication> apps)
    {
        TotalApplications = apps.Count;
        if (TotalApplications == 0)
        {
            AppliedCount = InterviewCount = OfferCount = RejectedCount = 0;
            ResponseRate = InterviewRate = OfferRate = "0%";
            return;
        }

        AppliedCount = apps.Count(a => a.Stage >= ApplicationStage.Applied);
        InterviewCount = apps.Count(a => a.Stage is ApplicationStage.Interview or ApplicationStage.Offer or ApplicationStage.Accepted);
        OfferCount = apps.Count(a => a.Stage is ApplicationStage.Offer or ApplicationStage.Accepted);
        RejectedCount = apps.Count(a => a.Stage == ApplicationStage.Rejected);

        var responded = apps.Count(a => a.Stage > ApplicationStage.Applied && a.Stage != ApplicationStage.Ghosted);
        ResponseRate = AppliedCount > 0 ? $"{responded * 100 / AppliedCount}%" : "0%";
        InterviewRate = AppliedCount > 0 ? $"{InterviewCount * 100 / AppliedCount}%" : "0%";
        OfferRate = AppliedCount > 0 ? $"{OfferCount * 100 / AppliedCount}%" : "0%";
    }

    private void UpdateFunnel(List<JobApplication> apps)
    {
        var stages = new[]
        {
            ApplicationStage.Saved, ApplicationStage.Applied, ApplicationStage.Screening,
            ApplicationStage.Interview, ApplicationStage.Offer, ApplicationStage.Accepted,
            ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Ghosted
        };

        var total = Math.Max(apps.Count, 1);
        FunnelData = new ObservableCollection<FunnelStageData>(
            stages.Select(s =>
            {
                var count = apps.Count(a => a.Stage == s);
                return new FunnelStageData(s, s.ToString(), count, count * 100.0 / total);
            })
            .Where(f => f.Count > 0));
    }

    private static ApplicationStage? GetNextStage(ApplicationStage current) => current switch
    {
        ApplicationStage.Saved => ApplicationStage.Applied,
        ApplicationStage.Applied => ApplicationStage.Screening,
        ApplicationStage.Screening => ApplicationStage.Interview,
        ApplicationStage.Interview => ApplicationStage.Offer,
        ApplicationStage.Offer => ApplicationStage.Accepted,
        _ => null
    };

    private static string FormatTimestamp(DateTimeOffset ts)
    {
        var ago = DateTimeOffset.UtcNow - ts;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalDays < 1) return $"{(int)ago.TotalHours}h ago";
        if (ago.TotalDays < 7) return $"{(int)ago.TotalDays}d ago";
        return ts.LocalDateTime.ToString("d MMM yyyy");
    }
}
