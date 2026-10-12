namespace lucidRESUME.Core.Models.Resume;

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Technologies { get; set; } = [];
    public List<ProjectEvidencePassage> EvidencePassages { get; set; } = [];
    public string? Url { get; set; }
    public DateOnly? Date { get; set; }

    /// <summary>Import sources that contributed to this entry.</summary>
    public List<string> ImportSources { get; set; } = [];

    /// <summary>
    /// Structured, source-specific observations retained for full-resolution JobML.
    /// These values support retrieval and audit; they are not human prose or claims.
    /// </summary>
    public Dictionary<string, string> EvidenceMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectEvidencePassage
{
    public string Text { get; set; } = "";
    public string? SourceUri { get; set; }
    public string? Revision { get; set; }
    public List<string> Skills { get; set; } = [];
}
