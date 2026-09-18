using YamlDotNet.Serialization;

namespace lucidRESUME.JobML;

public sealed class JobMlRoot
{
    [YamlMember(Alias = "jobml", Order = 0)]
    public JobMlHeader Header { get; set; } = new();

    [YamlIgnore]
    public string Version
    {
        get => Header.Version;
        set => Header.Version = value;
    }

    [YamlMember(Alias = "document", Order = 1)]
    public JobMlDocumentMetadata Document { get; set; } = new();

    [YamlMember(Alias = "entities", Order = 2)]
    public List<JobMlEntity> Entities { get; set; } = [];

    [YamlMember(Alias = "claims", Order = 3)]
    public List<JobMlClaim> Claims { get; set; } = [];

    [YamlMember(Alias = "concepts", Order = 4)]
    public List<JobMlConcept> Concepts { get; set; } = [];

    [YamlMember(Alias = "job", Order = 5)]
    public JobMlJob? Job { get; set; }

    [YamlMember(Alias = "requirements", Order = 6)]
    public List<JobMlRequirement> Requirements { get; set; } = [];

    /// <summary>
    /// Namespaced extension payloads. Core processors preserve these values but do not
    /// promote their observations or assessments into accepted claims.
    /// </summary>
    [YamlMember(Alias = "extensions", Order = 7)]
    public Dictionary<string, object?>? Extensions { get; set; }
}

/// <summary>
/// The semantic contract travels with the document so an unfamiliar language model can
/// understand the format without retrieving a separate specification.
/// </summary>
public sealed class JobMlHeader
{
    public static readonly IReadOnlyList<string> DefaultSemantics =
    [
        "Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.",
        "Every substantive claim should be supported by one or more evidence references.",
        "Evidence may reference human-readable prose in this document or an external resource.",
        "Do not infer unsupported claims, and do not treat aliases or machine-derived suggestions as evidence.",
        "When evaluating this resume, use both the human-readable prose and JobML.",
        "Treat JobML as a higher-resolution description of the resume, not as replacement prose."
    ];

    [YamlMember(Alias = "version", Order = 0)]
    public string Version { get; set; } = "0.1";

    [YamlMember(Alias = "purpose", Order = 1)]
    public string Purpose { get; set; } =
        "Machine-readable representation of claims made by this resume. Claims are supported by " +
        "human-readable prose or external evidence. Absence of a claim does not imply absence of a skill or capability.";

    [YamlMember(Alias = "semantics", Order = 2)]
    public List<string> Semantics { get; set; } = [.. DefaultSemantics];
}

public sealed class JobMlDocumentMetadata
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "resume";

    [YamlMember(Alias = "language")]
    public string Language { get; set; } = "en-GB";
}

public sealed class JobMlEntity
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "experience";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "source")]
    public string Source { get; set; } = "";
}

public sealed class JobMlClaim
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "subject")]
    public string Subject { get; set; } = "";

    [YamlMember(Alias = "statement")]
    public string Statement { get; set; } = "";

    [YamlMember(Alias = "concepts")]
    public JobMlClaimConcepts Concepts { get; set; } = new();

    [YamlMember(Alias = "supported_by")]
    public List<JobMlEvidence> Evidence { get; set; } = [];

    /// <summary>lucidRESUME extension. Inferred claims remain visibly derived until reviewed.</summary>
    [YamlMember(Alias = "origin")]
    public string? Origin { get; set; }

    /// <summary>lucidRESUME extension. A derived draft is not silently promoted to fact.</summary>
    [YamlMember(Alias = "review")]
    public string? Review { get; set; }
}

public sealed class JobMlClaimConcepts
{
    [YamlMember(Alias = "skills")]
    public List<string> Skills { get; set; } = [];

    [YamlMember(Alias = "capabilities")]
    public List<string> Capabilities { get; set; } = [];

    [YamlMember(Alias = "domains")]
    public List<string> Domains { get; set; } = [];

    [YamlIgnore]
    public IEnumerable<string> All => Skills.Concat(Capabilities).Concat(Domains);
}

public sealed class JobMlEvidence
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "prose";

    [YamlMember(Alias = "ref")]
    public string? Ref { get; set; }

    [YamlMember(Alias = "uri")]
    public string? Uri { get; set; }

    [YamlMember(Alias = "issuer")]
    public string? Issuer { get; set; }

    [YamlMember(Alias = "qualification")]
    public string? Qualification { get; set; }

    [YamlMember(Alias = "fingerprint")]
    public JobMlFingerprint? Fingerprint { get; set; }

    /// <summary>
    /// A compact YAML representation of W3C Web Annotation text selectors.
    /// The stable ref is primary; this selector repairs and disambiguates it after edits.
    /// </summary>
    [YamlMember(Alias = "selector")]
    public JobMlTextSelector? Selector { get; set; }

    [YamlMember(Alias = "state")]
    public string? State { get; set; }
}

public sealed class JobMlFingerprint
{
    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "";
}

public sealed class JobMlTextSelector
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "TextQuoteSelector";

    [YamlMember(Alias = "exact")]
    public string Exact { get; set; } = "";

    [YamlMember(Alias = "prefix")]
    public string? Prefix { get; set; }

    [YamlMember(Alias = "suffix")]
    public string? Suffix { get; set; }
}

public sealed class JobMlConcept
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "skill";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "aliases")]
    public List<string> Aliases { get; set; } = [];
}

public sealed class JobMlJob
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";
}

public sealed class JobMlRequirement
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "concept")]
    public string Concept { get; set; } = "";

    [YamlMember(Alias = "importance")]
    public string Importance { get; set; } = "required";
}

public sealed record JobMlFile(string Markdown, JobMlRoot Data);

public enum JobMlDiagnosticSeverity { Info, Warning, Error }

public sealed record JobMlDiagnostic(
    JobMlDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Path = null);

public enum EvidenceState { Valid, Changed, Missing, Ambiguous, External }

public sealed record EvidenceResolution(
    JobMlEvidence Evidence,
    EvidenceState State,
    string? CurrentText = null,
    string? SuggestedReference = null);

public sealed record ClaimEvidenceResolution(
    JobMlClaim Claim,
    IReadOnlyList<EvidenceResolution> Evidence);

public enum CoverageState { Direct, Ambiguous, None }

public sealed record JobMlCoverageEntry(
    JobMlRequirement Requirement,
    CoverageState State,
    IReadOnlyList<JobMlClaim> Claims);
