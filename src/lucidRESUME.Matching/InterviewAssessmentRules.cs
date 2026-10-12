using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;

namespace lucidRESUME.Matching;

public sealed class InterviewAssessmentRules : IInterviewAssessor
{
    public Task<InterviewAssessment> AssessAsync(InterviewAssessmentRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var probe = request.RoleId is null ? InterviewProbe.Context
            : Regex.IsMatch(request.Action, @"\b(no experience|never used|have not used|haven't used)\b", RegexOptions.IgnoreCase) ? InterviewProbe.Skip
            : string.IsNullOrWhiteSpace(request.Action) || WordCount(request.Action) < 5 ? InterviewProbe.Contribution
            : Regex.IsMatch(request.Action, @"\b(we|our team|the team)\b", RegexOptions.IgnoreCase) &&
              !Regex.IsMatch(request.Action, @"\b(I|my|personally)\b", RegexOptions.IgnoreCase) ? InterviewProbe.Contribution
            : string.IsNullOrWhiteSpace(request.Context) ? InterviewProbe.Context
            : string.IsNullOrWhiteSpace(request.Outcome) || Regex.IsMatch(request.Outcome,
                @"\b(planned|intended|hope|hoped|expect|expected|aim|aimed|would)\b", RegexOptions.IgnoreCase) ? InterviewProbe.Outcome
            : InterviewProbe.Review;
        return Task.FromResult(Render(request, probe, "Suggested follow-up based on the details supplied; this does not verify the account."));
    }

    public static InterviewAssessment Render(InterviewAssessmentRequest request, InterviewProbe probe, string feedback) =>
        new(probe, QuestionFor(probe, request), feedback, Hash(request));

    public static string Hash(InterviewAssessmentRequest request) => EvidenceLedgerBuilder.FastHash(JsonSerializer.Serialize(request));

    public static string QuestionFor(InterviewProbe probe, InterviewAssessmentRequest request) => probe switch
    {
        InterviewProbe.Context => request.RoleId is null ? "Which role does this example belong to?"
            : "What problem or constraint were you working with, and who was affected?",
        InterviewProbe.Contribution => "What did you personally do in this example? Separate your contribution from the team's work and retain any limits on your responsibility.",
        InterviewProbe.Decision => "Which decision or trade-off was yours, what alternatives did you consider, and why did you choose that approach?",
        InterviewProbe.Outcome => "What actually happened after your contribution? A concrete qualitative result is useful; if the outcome is unknown or only intended, say so.",
        InterviewProbe.Verification => "How did you observe or check the outcome? Mention a source or measurement only if you have one; do not estimate a number to fill the gap.",
        InterviewProbe.Relevance => $"How does this example relate to ‘{request.Requirement}’? Describe direct experience or explain the limits of the connection.",
        InterviewProbe.Review => "Review the prose for who did the work, the actual outcome and any uncertainty. Edit it before saving it to the role transcript.",
        _ => "Leave this requirement as a gap if you do not have an example. You can skip this question and continue with your current evidence."
    };

    private static int WordCount(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
