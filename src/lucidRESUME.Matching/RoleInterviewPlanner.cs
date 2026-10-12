using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed record RoleInterviewTopic(string Key, string Requirement, string Reason,
    Guid? SuggestedRoleId, string? ExistingEvidence, string? Prompt = null);

/// <summary>Explainable authoring decisions, not a proficiency or eligibility score.</summary>
public static class RoleInterviewPlanner
{
    public static IReadOnlyList<RoleInterviewTopic> Plan(ResumeDocument resume, CoverageReport coverage)
    {
        var topics = new List<RoleInterviewTopic>();
        foreach (var entry in coverage.Entries.OrderBy(e => e.Requirement.Priority switch
                 { RequirementPriority.Required => 0, RequirementPriority.Responsibility => 1, _ => 2 }))
        {
            // A technology label establishes a mention, not a usable account of work.
            var narrative = entry.EvidenceSection?.Contains("Achievements[", StringComparison.Ordinal) == true;
            if (entry.IsCovered && narrative && HasDetail(entry.Evidence!) &&
                (entry.Requirement.Priority != RequirementPriority.Required || entry.IsDirectMatch)) continue;
            Guid? roleId = null;
            var match = Regex.Match(entry.EvidenceSection ?? "", @"^Experience\[(\d+)\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index) && index < resume.Experience.Count)
                roleId = RoleEvidenceQuality.HasRoleIdentity(resume.Experience[index]) ? resume.Experience[index].Id : null;
            topics.Add(new("requirement:" + entry.Requirement.Text.ToLowerInvariant(), entry.Requirement.Text,
                entry.Evidence is not null && !entry.IsDirectMatch && entry.Requirement.Priority != RequirementPriority.Responsibility
                        ? "Related evidence was found, but it does not confirm this requirement. Describe a real example if you have one."
                    : !entry.IsCovered ? "No supporting account found. Check whether you have a real example; skipping leaves the gap visible."
                    : narrative ? "The matching account needs more detail about your contribution or outcome."
                    : "A skill is mentioned, but there is no concrete account of how you used it.",
                roleId, entry.Evidence));
        }
        foreach (var role in resume.Experience.Where(RoleEvidenceQuality.HasRoleIdentity).OrderByDescending(r => r.StartDate)
                     .Where(r => !topics.Any(t => t.SuggestedRoleId == r.Id))
                     .Where(r => coverage.Entries.Count == 0 || coverage.Entries.Any(e =>
                         r.Technologies.Contains(e.Requirement.Text, StringComparer.OrdinalIgnoreCase) ||
                         (r.Title ?? "").Contains(e.Requirement.Text, StringComparison.OrdinalIgnoreCase)))
                     .Where(r => r.Achievements.Count == 0 || r.Achievements.All(a => !HasDetail(a))))
            topics.Add(new("role:" + role.Id.ToString("N"), $"Your work as {role.Title} at {role.Company}",
                "This role has little descriptive evidence. Add a specific example that matters for the target job.",
                role.Id, role.Achievements.FirstOrDefault()));
        topics.InsertRange(0, resume.Experience.Where(RoleEvidenceQuality.HasRoleIdentity).Where(RoleEvidenceQuality.NeedsStatusClarification)
            .Select(role => new RoleInterviewTopic($"role:{role.Id:N}:status", $"Delivery status and timeline: {role.Title} at {role.Company}",
                "The source accounts mention both a delivered milestone and cancellation. Confirm whether both happened, and in what order.",
                role.Id, string.Join(" ", role.Achievements), StatusPrompt)));
        return topics.DistinctBy(t => t.Key).ToList();
    }

    public static IReadOnlyList<RoleInterviewTopic> PlanCompleteTranscript(ResumeDocument resume)
    {
        (string Key, string Label, string Prompt)[] dimensions =
        [
            ("status", "Delivery status and timeline", StatusPrompt),
            ("scope", "Role scope and responsibility", "Who was the work for, what did the organisation need, and what were you personally accountable for? Include team size or scale only when known. Confirm title, dates and client or company identity if the imports differ."),
            ("delivery", "A concrete delivery example", "Describe one thing you personally built, changed or operated. Explain the workflow, users, your contribution and whether it was a prototype, pilot or production system."),
            ("decisions", "Architecture, decisions and tools", "Which architectural or implementation decisions were yours? Name the tools and skills actually used, alternatives considered, constraints and trade-offs."),
            ("outcomes", "Outcomes and supporting evidence", "What happened as a result? Explain how you observed or measured it and any supporting source. An unknown outcome or qualitative result is valid; do not invent a metric."),
            ("leadership", "Collaboration and technical leadership", "How did you work with others, review designs, develop engineers or resolve disagreements? Separate team achievements from your contribution and distinguish influence from formal management."),
            ("learning", "Reliability, constraints and learning", "Describe a failure, incident or difficult constraint, how you handled it, and what changed afterwards. Which safeguards, tests, recovery mechanisms or working practices did you actually implement?")
        ];
        return resume.Experience.Where(RoleEvidenceQuality.HasRoleIdentity).OrderByDescending(role => role.IsCurrent).ThenByDescending(role => role.StartDate)
            .SelectMany(role => dimensions.Where(dimension => (dimension.Key != "status" || RoleEvidenceQuality.NeedsStatusClarification(role)) && !role.InterviewEntries.Any(entry =>
                    entry.TopicKey.StartsWith("role:", StringComparison.Ordinal) &&
                    entry.TopicKey.EndsWith(":" + dimension.Key, StringComparison.Ordinal)))
                .Select(dimension => new RoleInterviewTopic($"role:{role.Id:N}:{dimension.Key}",
                    $"{dimension.Label}: {role.Title} at {role.Company}",
                    "Build a reusable account of this role. Add actual details, or skip dimensions you cannot substantiate.",
                    role.Id, string.Join(" ", role.Achievements), dimension.Prompt))).ToList();
    }

    private const string StatusPrompt = "One source describes a delivered milestone, while another describes cancellation or a change of direction. What was actually completed, what remained planned, and what happened afterwards? Clarify the dates and your contribution; leave uncertain facts explicitly uncertain.";

    // Only an interview trigger. Numbers are deliberately not a prerequisite.
    private static bool HasDetail(string text) =>
        !Regex.IsMatch(text, @"^(responsible for|worked on|helped with|duties included)\b", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(text, @"\b(built|led|designed|delivered|implemented|introduced|reduced|improved|resolved|automated|wrote|supported|maintained|created|migrated|trained|mentored)\b", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(text, @"\b(reduced|improved|resolved|enabled|eliminated|saved|increased|prevented|restored|simplified|so that|resulting|allowing|which|without|to help)\b", RegexOptions.IgnoreCase);

    public static string Draft(string context, string action, string outcome) =>
        string.Join(" ", new[] { action, context, outcome }.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().TrimEnd('.') + "."));

    public static string ProbeFor(string requirement, string? targetTitle)
    {
        // Ask about the requirement first; use the target role only for an otherwise generic topic.
        string Probe(string text)
        {
            if (Regex.IsMatch(text, @"\b(production agent|agentic|agent architecture|harness|runtime)\b", RegexOptions.IgnoreCase))
                return "Describe one system you personally shipped. Is it live, a pilot or a prototype? Explain its workflow, users, context and memory, tools it calls, state it can change, and your own architecture or implementation decisions.";
            if (Regex.IsMatch(text, @"\b(evaluation|experimentation|evals?|regression)\b", RegexOptions.IgnoreCase))
                return "Describe an evaluation you built or used: the cases and their source, deterministic checks or model/human review, failures it caught, and how the result informed a release or production experiment. Distinguish actual tests from planned work.";
            if (Regex.IsMatch(text, @"\b(safe|safety|authorization|idempotency|recovery)\b", RegexOptions.IgnoreCase))
                return "Which consequential actions could the system perform? Explain the permissions, validation, state transitions, audit trail, duplicate-action protection, recovery and human escalation you personally implemented. Say which controls are absent or planned.";
            if (Regex.IsMatch(text, @"\b(model judgment|model judgement|model strategy|routing|open-weight|fine-tuning)\b", RegexOptions.IgnoreCase))
                return "Describe a model decision you made: candidates, representative tasks, quality, latency, cost and operational trade-offs; routing or fallback; and what you measured. Distinguish using a model from fine-tuning or hosting it.";

            if (Regex.IsMatch(text, @"\b(lead|led|leadership|director|head|cto|vp|mentor|hiring|team)\b", RegexOptions.IgnoreCase))
                return "Which decisions were yours? Describe team scope, how you influenced or supported others, and what changed. Distinguish your contribution from the team's result.";
            if (Regex.IsMatch(text, @"\b(product|commercial|sales|customer|revenue)\b", RegexOptions.IgnoreCase))
                return "Who was the work for? Explain the user or business problem, the choices you made, and the observed adoption, customer or business outcome.";
            if (Regex.IsMatch(text, @"\b(performance|latency|cost|optimisation|optimization|efficiency)\b", RegexOptions.IgnoreCase))
                return "What was the baseline or problem, what did you change, and how did you check the outcome? Include a before/after measure only if you can substantiate it.";
            if (Regex.IsMatch(text, @"\b(platform|cloud|aws|azure|kubernetes|terraform|security|reliability|incident)\b", RegexOptions.IgnoreCase))
                return "What did you personally design, implement or operate? Describe the constraints, an important technical decision, and its observed effect on users, delivery or reliability.";
            return "";
        }
        var probe = Probe(requirement);
        if (probe.Length == 0) probe = Probe(targetTitle ?? "");
        return probe.Length > 0 ? probe : "Choose one specific example. Explain your contribution, why you chose that approach, and what happened. Do not imply experience you do not have.";
    }
}
