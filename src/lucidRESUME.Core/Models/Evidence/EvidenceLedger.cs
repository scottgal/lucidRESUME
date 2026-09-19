namespace lucidRESUME.Core.Models.Evidence;

/// <summary>
/// Persisted source of truth produced when resume sources are ingested. Output documents
/// are projections of this ledger; renderers must not infer new evidence relationships.
/// </summary>
public sealed class EvidenceLedger
{
    public string SourceRevision { get; set; } = "";
    public DateTimeOffset BuiltAt { get; set; }
    public List<EvidenceRecord> Evidence { get; set; } = [];
    public List<LedgerClaim> Claims { get; set; } = [];
}

public sealed class EvidenceRecord
{
    public string Id { get; set; } = "";
    public Guid SourceResumeId { get; set; }
    public string SourceName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Locator { get; set; } = "";
    public string Text { get; set; } = "";
    public string FastHash { get; set; } = "";
    public string? ExternalUri { get; set; }
    public string? Title { get; set; }
    public List<string> Authors { get; set; } = [];
    public string? Publisher { get; set; }
    public DateOnly? PublishedOn { get; set; }
    public DateOnly? AccessedOn { get; set; }
    public string ExtractionMethod { get; set; } = "deterministic";
    public double Confidence { get; set; } = 1.0;
}

public sealed class LedgerClaim
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Statement { get; set; } = "";
    public string? SubjectId { get; set; }
    public List<string> EvidenceIds { get; set; } = [];
    public List<string> Concepts { get; set; } = [];
    public string Origin { get; set; } = "ingested";
    public string Review { get; set; } = "required";
}
