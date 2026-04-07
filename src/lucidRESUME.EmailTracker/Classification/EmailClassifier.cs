using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.EmailTracker.Classification;

/// <summary>
/// Rule-based email classifier. Detects job-related emails by subject/body patterns.
/// </summary>
public static class EmailClassifier
{
    private static readonly (string[] Patterns, ApplicationStage Stage, TimelineEventType EventType, float Confidence)[] Rules =
    [
        // Offer - high confidence, check first
        (["offer letter", "pleased to offer", "compensation package", "we would like to extend"],
            ApplicationStage.Offer, TimelineEventType.OfferReceived, 0.9f),

        // Interview invite
        (["interview", "schedule a call", "availability for", "calendar invite", "technical assessment",
          "coding challenge", "onsite", "video call", "meet the team"],
            ApplicationStage.Interview, TimelineEventType.InterviewScheduled, 0.8f),

        // Rejection
        (["unfortunately", "we have decided to move forward with other candidates",
          "not selected", "position has been filled", "will not be moving forward",
          "decided not to proceed", "other applicants", "not be progressing"],
            ApplicationStage.Rejected, TimelineEventType.EmailReceived, 0.85f),

        // Screening / phone screen
        (["phone screen", "initial call", "recruiter call", "introductory call",
          "brief chat", "15 minute", "30 minute call"],
            ApplicationStage.Screening, TimelineEventType.EmailReceived, 0.75f),

        // Confirmation / receipt
        (["application received", "application has been received",
          "thank you for applying", "we received your application",
          "application has been submitted", "confirming your application",
          "successfully applied", "your application for"],
            ApplicationStage.Applied, TimelineEventType.EmailReceived, 0.7f),
    ];

    public static ClassificationResult Classify(ScannedEmail email)
    {
        var searchText = $"{email.Subject} {email.BodyPreview}";

        foreach (var (patterns, stage, eventType, confidence) in Rules)
        {
            foreach (var pattern in patterns)
            {
                if (searchText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new ClassificationResult
                    {
                        IsJobRelated = true,
                        Confidence = confidence,
                        SuggestedEventType = eventType,
                        SuggestedStage = stage,
                        MatchedKeyword = pattern
                    };
                }
            }
        }

        // Generic job-related signals (lower confidence)
        var genericPatterns = new[] { "job", "position", "role", "vacancy", "hiring", "recruit" };
        foreach (var p in genericPatterns)
        {
            if (email.Subject.Contains(p, StringComparison.OrdinalIgnoreCase))
            {
                return new ClassificationResult
                {
                    IsJobRelated = true,
                    Confidence = 0.4f,
                    SuggestedEventType = TimelineEventType.EmailReceived,
                    SuggestedStage = null,
                    MatchedKeyword = p
                };
            }
        }

        return new ClassificationResult { IsJobRelated = false };
    }
}