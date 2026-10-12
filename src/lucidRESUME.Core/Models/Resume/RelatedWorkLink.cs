namespace lucidRESUME.Core.Models.Resume;

/// <summary>A short publication/project citation, with its original support retained.</summary>
public sealed record RelatedWorkLink
{
    public Guid SourceId { get; init; }
    public string Kind { get; init; } = "project";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string Relevance { get; init; } = "";
    public string SupportingPassage { get; init; } = "";
    public string SourceRevision { get; init; } = "";
    public List<string> Requirements { get; init; } = [];
    public List<string> ClaimIds { get; init; } = [];
    public List<string> EvidenceIds { get; init; } = [];
    public string Attribution { get; init; } = "unknown";
    public string Review { get; init; } = "accepted";
}
