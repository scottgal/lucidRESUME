using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Persistence;

/// <summary>The authoritative, editable career. Imports remain separate evidence archives.</summary>
public sealed class CompleteCareerRecord
{
    public const string ContentType = "application/vnd.lucidresume.career-transcript";
    public ResumeDocument Transcript { get; set; } = new();
    public Dictionary<Guid, string> ImportRevisions { get; set; } = [];
    public string OverridesRevision { get; set; } = "";
    public CareerSemanticIndex? SemanticIndex { get; set; }
    public List<string> ReviewWarnings { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Derived vectors, partitioned by their exact model identity; never eligibility evidence.</summary>
public sealed class CareerSemanticIndex
{
    public string Model { get; set; } = "";
    public string? ModelDigest { get; set; }
    public string Normalization { get; set; } = "";
    public Dictionary<string, float[]> Concepts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
