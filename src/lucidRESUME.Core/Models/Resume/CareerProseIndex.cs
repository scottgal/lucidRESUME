namespace lucidRESUME.Core.Models.Resume;

/// <summary>Derived, reusable analysis. It never replaces the original claims or prose.</summary>
public sealed class CareerProseIndex
{
    public const string CurrentPolicyVersion = "career-segments-native-v4";
    public string SourceRevision { get; set; } = "";
    public string AcceptedClaimsRevision { get; set; } = "";
    public string PolicyVersion { get; set; } = CurrentPolicyVersion;
    public string EmbeddingSpace { get; set; } = "";
    public string EmbeddingRevision { get; set; } = "";
    public Dictionary<string, int> DocumentFrequency { get; set; } = new(StringComparer.Ordinal);
    [System.Text.Json.Serialization.JsonIgnore]
    public string SearchRevision => SourceRevision + "|" + AcceptedClaimsRevision + "|" + PolicyVersion;
    public DateTimeOffset AnalysedAt { get; set; }
    public List<CareerProseSegment> Segments { get; set; } = [];
}

public sealed class CareerProseSegment
{
    public string Id { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> ClaimIds { get; set; } = [];
    public List<string> EvidenceIds { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> SkillConceptIds { get; set; } = [];
    public List<ContributionAccount> Accounts { get; set; } = [];
    public List<string> MissingDimensions { get; set; } = [];
    public float[] Embedding { get; set; } = [];
    public string EmbeddingSpace { get; set; } = "";
    public string FactSignature { get; set; } = "";
    public double Salience { get; set; }
    public Dictionary<string, int> TermFrequency { get; set; } = new(StringComparer.Ordinal);
}
