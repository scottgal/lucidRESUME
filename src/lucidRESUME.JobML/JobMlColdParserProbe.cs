namespace lucidRESUME.JobML;

/// <summary>
/// Builds a provider-neutral cold-parser challenge. Only Document and Questions are sent to a
/// model; GroundTruth is retained by the evaluator.
/// </summary>
public static class JobMlColdParserProbe
{
    public static JobMlColdParserChallenge Create(string document, JobMlFile file)
    {
        var reconciled = JobMlProcessor.Reconcile(file);
        return new JobMlColdParserChallenge(
            document,
            [
                "What experience, skills, capabilities, responsibilities, or domain knowledge does this person claim?",
                "For each claim, identify the evidence that supports it.",
                "Which claims are unsupported, stale, missing evidence, or still require human review?",
                "Which exact human-readable prose supports each prose-backed claim?",
                "Do not add claims that are merely plausible or implied by aliases."
            ],
            reconciled.Select(item => new JobMlColdParserExpectedClaim(
                item.Claim.Id,
                item.Claim.Statement,
                item.Claim.Review,
                item.Claim.Concepts.All.ToList(),
                item.Evidence.Select(evidence => new JobMlColdParserExpectedEvidence(
                    evidence.Evidence.Ref ?? evidence.Evidence.Uri,
                    evidence.State.ToString().ToLowerInvariant(),
                    evidence.CurrentText)).ToList())).ToList());
    }
}

public sealed record JobMlColdParserChallenge(
    string Document,
    IReadOnlyList<string> Questions,
    IReadOnlyList<JobMlColdParserExpectedClaim> GroundTruth);

public sealed record JobMlColdParserExpectedClaim(
    string Id,
    string Statement,
    string? Review,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<JobMlColdParserExpectedEvidence> Evidence);

public sealed record JobMlColdParserExpectedEvidence(
    string? Reference,
    string State,
    string? Prose);
