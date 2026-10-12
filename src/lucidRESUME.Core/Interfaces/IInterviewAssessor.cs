namespace lucidRESUME.Core.Interfaces;

public enum InterviewProbe { Context, Contribution, Decision, Outcome, Verification, Relevance, Review, Skip }

public sealed record InterviewAssessmentRequest(Guid? RoleId, string Requirement, string TargetTitle,
    string ExistingEvidence, string Context, string Action, string Outcome);

/// <summary>Advisory authoring feedback. It cannot accept prose, assess competence or create evidence.</summary>
public sealed record InterviewAssessment(InterviewProbe Probe, string Question, string Feedback,
    string InputHash, string Provider = "rules", string? Model = null, double? Probability = null,
    IReadOnlyDictionary<string, double>? Probabilities = null, bool UsedModel = false);

public interface IInterviewAssessor
{
    Task<InterviewAssessment> AssessAsync(InterviewAssessmentRequest request, CancellationToken ct = default);
}
