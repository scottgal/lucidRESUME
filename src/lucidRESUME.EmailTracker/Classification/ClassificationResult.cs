using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.EmailTracker.Classification;

public sealed class ClassificationResult
{
    public bool IsJobRelated { get; init; }
    public float Confidence { get; init; }
    public TimelineEventType SuggestedEventType { get; init; }
    public ApplicationStage? SuggestedStage { get; init; }
    public string? MatchedKeyword { get; init; }
}
