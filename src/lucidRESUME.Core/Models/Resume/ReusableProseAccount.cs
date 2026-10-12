namespace lucidRESUME.Core.Models.Resume;

/// <summary>A derived wording of source-backed prose; never additional career evidence.</summary>
public sealed class ReusableProseAccount
{
    public string Key { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string Text { get; set; } = "";
    public bool RetainedSource { get; set; }
    public List<string> ReviewNotes { get; set; } = [];
    public string RejectionReason { get; set; } = "";
    public List<string> Skills { get; set; } = [];
    public List<string> ClaimIds { get; set; } = [];
    public List<string> EvidenceIds { get; set; } = [];
    public List<string> SubjectIds { get; set; } = [];
    public string Provider { get; set; } = "";
    public string ModelIdentity { get; set; } = "";
    public string Pass { get; set; } = "";
    public string JobBrief { get; set; } = "";
    public int MaximumWords { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
