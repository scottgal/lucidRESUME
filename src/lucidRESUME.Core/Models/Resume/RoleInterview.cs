using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>Author-reviewed interview material retained with its original role.</summary>
public sealed class RoleInterviewEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TopicKey { get; set; } = "";
    public List<string> Skills { get; set; } = [];
    public string Requirement { get; set; } = "";
    public string Context { get; set; } = "";
    public string Action { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string ReviewedProse { get; set; } = "";
    public DateTimeOffset AcceptedAt { get; set; }
    public InterviewAssessment? Assessment { get; set; }
    public ContributionAccount? Account { get; set; }
}
