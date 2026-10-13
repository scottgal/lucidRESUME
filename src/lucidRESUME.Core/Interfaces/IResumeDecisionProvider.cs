namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Makes one bounded, non-generative decision about already-extracted resume text.
/// Implementations select only from caller-owned candidates and never mutate a resume.
/// </summary>
public interface IResumeDecisionProvider
{
    string ProviderName { get; }

    /// <summary>Classifies a bounded set sharing source context; adapters may submit one native multi-question request.</summary>
    async Task<IReadOnlyList<ResumeDecisionResult>> DecideBatchAsync(
        IReadOnlyList<ResumeDecisionRequest> requests, CancellationToken ct = default)
    {
        var results = new List<ResumeDecisionResult>();
        foreach (var request in requests) results.Add(await DecideAsync(request, ct));
        return results;
    }

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
