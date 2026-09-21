namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Makes one bounded, non-generative decision about already-extracted resume text.
/// Implementations select only from caller-owned candidates and never mutate a resume.
/// </summary>
public interface IResumeDecisionProvider
{
    string ProviderName { get; }

    Task<ResumeDecisionResult> DecideAsync(
        ResumeDecisionRequest request,
        CancellationToken ct = default);
}

public sealed record ResumeDecisionRequest(
    string DecisionId,
    string SourceRef,
    string SourceHash,
    string State,
    string Instructions,
    IReadOnlyDictionary<string, string> Candidates);

public sealed record ResumeDecisionResult(
    string SelectedCandidate,
    IReadOnlyDictionary<string, double> Probabilities,
    double Confidence,
    string Provider,
    string Model,
    int InputTokens = 0,
    int OutputTokens = 0,
    string? RequestId = null);
