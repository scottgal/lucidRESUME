using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>Author-supplied facts kept beside reviewed prose. Empty fields mean unknown.</summary>
public sealed record ContributionAccount
{
    public string Ownership { get; init; } = "";
    public string Action { get; init; } = "";
    public string Object { get; init; } = "";
    public string Context { get; init; } = "";
    public string Decision { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string DeliveryStatus { get; init; } = "";
    public List<string> Qualifiers { get; init; } = [];
    public List<string> Skills { get; init; } = [];
    public string ReviewedProse { get; init; } = "";
    public Guid RoleId { get; init; }
    public Guid InterviewId { get; init; }
}

/// <summary>Cheap advisory cues, not semantic role labelling or verification of a claim.</summary>
public static class ProseStructure
{
    private static readonly Regex Action = new(@"\b(built|designed|implemented|operated|delivered|introduced|reduced|improved|resolved|automated|wrote|supported|maintained|created|migrated|trained|mentored|helped|contributed|led|managed)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Status = new(@"\b(prototype|pilot|production|planned|ongoing|cancelled|canceled|not yet|unknown|unmeasured)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Outcome = new(@"\b(reduced|improved|resolved|enabled|saved|increased|prevented|restored|simplified|result|outcome|so that|without|unknown|unmeasured|not yet measured)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> MissingDimensions(string prose)
    {
        var missing = new List<string>();
        if (!Action.IsMatch(prose)) missing.Add("action");
        if (!Regex.IsMatch(prose, @"\b(I|personally|my|helped|assisted|supported|contributed|led|managed)\b", RegexOptions.IgnoreCase)) missing.Add("ownership");
        if (!Outcome.IsMatch(prose) && !Status.IsMatch(prose)) missing.Add("outcome-or-status");
        return missing;
    }

    public static string NextQuestion(string prose) => MissingDimensions(prose).FirstOrDefault() switch
    {
        "action" => "What did you personally build, change, investigate or operate? Name the thing and one concrete example.",
        "ownership" => "Which part was your own contribution, and which part belonged to the team?",
        "outcome-or-status" => "What happened, or what is its current status? An unmeasured outcome, prototype or ongoing work is valid.",
        _ => "Which decision or constraint best explains your contribution? Add detail only if it is supported."
    };
}
