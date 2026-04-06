using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.Core.Persistence;
using lucidRESUME.EmailTracker.Classification;
using lucidRESUME.EmailTracker.Matching;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.EmailTracker;

public sealed class ScanSummary
{
    public int EmailsScanned { get; set; }
    public int JobRelatedFound { get; set; }
    public int Matched { get; set; }
    public int TimelineEventsCreated { get; set; }
    public int StageAdvances { get; set; }
    public List<string> UnmatchedEmails { get; set; } = [];
}

public sealed class EmailScanOrchestrator
{
    private readonly IEmailScanner _scanner;
    private readonly IAppStore _store;
    private readonly ILogger<EmailScanOrchestrator> _logger;

    // Terminal stages — never auto-advance past these
    private static readonly HashSet<ApplicationStage> TerminalStages =
    [
        ApplicationStage.Accepted,
        ApplicationStage.Rejected,
        ApplicationStage.Withdrawn,
        ApplicationStage.Ghosted
    ];

    public EmailScanOrchestrator(
        IEmailScanner scanner,
        IAppStore store,
        ILogger<EmailScanOrchestrator> logger)
    {
        _scanner = scanner;
        _store = store;
        _logger = logger;
    }

    public bool IsConfigured => _scanner.IsConfigured;

    public async Task<ScanSummary> ScanAsync(CancellationToken ct = default)
    {
        var summary = new ScanSummary();

        if (!_scanner.IsConfigured)
        {
            _logger.LogWarning("Email scanner not configured — skipping scan");
            return summary;
        }

        var state = await _store.LoadAsync(ct);
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        // Collect existing message IDs to avoid duplicates
        var seenMessageIds = new HashSet<string>(
            state.Applications
                .SelectMany(a => a.Timeline)
                .Where(e => e.EmailMessageId is not null)
                .Select(e => e.EmailMessageId!));

        var emails = await _scanner.ScanAsync(since, ct);
        summary.EmailsScanned = emails.Count;

        _logger.LogInformation("Scanned {Count} emails since {Since}", emails.Count, since);

        var eventsToApply = new List<(Guid ApplicationId, TimelineEvent Event, ApplicationStage? SuggestedStage)>();

        foreach (var email in emails)
        {
            if (seenMessageIds.Contains(email.MessageId))
                continue;

            var classification = EmailClassifier.Classify(email);
            if (!classification.IsJobRelated)
                continue;

            summary.JobRelatedFound++;

            var match = EmailApplicationMatcher.Match(email, state.Applications);
            if (match.Application is null || match.Confidence < 0.5f)
            {
                summary.UnmatchedEmails.Add($"{email.Subject} (from {email.From})");
                continue;
            }

            summary.Matched++;

            var evt = new TimelineEvent
            {
                Type = classification.SuggestedEventType,
                Title = classification.SuggestedStage.HasValue
                    ? $"Email: {classification.SuggestedStage.Value}"
                    : "Email received",
                Description = email.Subject,
                EmailMessageId = email.MessageId,
                EmailSubject = email.Subject,
                EmailFrom = email.From,
                Timestamp = email.Date,
                IsAutoDetected = true
            };

            eventsToApply.Add((match.Application.ApplicationId, evt, classification.SuggestedStage));
        }

        if (eventsToApply.Count == 0)
            return summary;

        // Apply all events atomically
        await _store.MutateAsync(s =>
        {
            foreach (var (appId, evt, suggestedStage) in eventsToApply)
            {
                var app = s.Applications.FirstOrDefault(a => a.ApplicationId == appId);
                if (app is null) continue;

                app.Timeline.Add(evt);
                app.LastActivityAt = evt.Timestamp > (app.LastActivityAt ?? DateTimeOffset.MinValue)
                    ? evt.Timestamp
                    : app.LastActivityAt;
                summary.TimelineEventsCreated++;

                // Auto-advance stage if the suggested stage is ahead and not terminal
                if (suggestedStage.HasValue &&
                    suggestedStage.Value > app.Stage &&
                    !TerminalStages.Contains(app.Stage))
                {
                    // Never auto-accept
                    if (suggestedStage.Value == ApplicationStage.Accepted)
                        continue;

                    _logger.LogInformation("Auto-advancing {Company} from {From} to {To}",
                        app.CompanyName, app.Stage, suggestedStage.Value);

                    app.AdvanceTo(suggestedStage.Value);
                    // Mark the stage-change event as auto-detected too
                    app.Timeline.Last().IsAutoDetected = true;
                    summary.StageAdvances++;
                }
            }
        }, ct);

        _logger.LogInformation(
            "Scan complete: {Scanned} emails, {Related} job-related, {Matched} matched, {Events} events, {Advances} stage advances",
            summary.EmailsScanned, summary.JobRelatedFound, summary.Matched,
            summary.TimelineEventsCreated, summary.StageAdvances);

        return summary;
    }
}
