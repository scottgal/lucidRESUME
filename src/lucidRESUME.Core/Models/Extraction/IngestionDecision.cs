namespace lucidRESUME.Core.Models.Extraction;

/// <summary>
/// Audit record for a bounded model decision made during ingestion. The source text is
/// deliberately excluded; SourceHash detects drift without duplicating personal data.
/// </summary>
public sealed class IngestionDecision
{
    public string DecisionId { get; set; } = "";
    public string ContractVersion { get; set; } = "";
    public string SourceRef { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string CandidateSetHash { get; set; } = "";
    public string SelectedCandidate { get; set; } = "";
    public string? SelectedValue { get; set; }
    public Dictionary<string, double> Probabilities { get; set; } = new(StringComparer.Ordinal);
    public double Confidence { get; set; }
    public double Margin { get; set; }
    public bool Accepted { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? RequestId { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}
