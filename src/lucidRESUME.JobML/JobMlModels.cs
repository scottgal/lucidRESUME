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

    /// <summary>Source catalogue for a career_record profile.</summary>
    [YamlMember(Alias = "sources", Order = 5)]
    public List<JobMlSource> Sources { get; set; } = [];

    /// <summary>Model-specific semantic spaces. They are derived indexes, never evidence.</summary>
    [YamlMember(Alias = "semantic_spaces", Order = 6)]
    public List<JobMlSemanticSpace> SemanticSpaces { get; set; } = [];

    /// <summary>Derived role vectors used for matching, reproducible from their seed concepts.</summary>
    [YamlMember(Alias = "role_centroids", Order = 7)]
    public List<JobMlRoleCentroid> RoleCentroids { get; set; } = [];

    [YamlMember(Alias = "job", Order = 8)]
    public JobMlJob? Job { get; set; }

    [YamlMember(Alias = "requirements", Order = 9)]
    public List<JobMlRequirement> Requirements { get; set; } = [];

    /// <summary>
    /// Namespaced extension payloads. Core processors preserve these values but do not
    /// promote their observations or assessments into accepted claims.
    /// </summary>
    [YamlMember(Alias = "extensions", Order = 10)]
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
        "Embeddings, centroids, and other derived indexes assist retrieval but are not evidence.",
        "When evaluating this resume, use both the human-readable prose and JobML.",
        "Treat JobML as a higher-resolution description of the resume, not as replacement prose."
    ];

    [YamlMember(Alias = "version", Order = 0)]
    public string Version { get; set; } = "0.1";

    /// <summary>career_record for a complete export; resume for a role-specific projection.</summary>
    [YamlMember(Alias = "profile", Order = 1)]
    public string Profile { get; set; } = "resume";

    [YamlMember(Alias = "purpose", Order = 2)]
    public string Purpose { get; set; } =
        "Machine-readable representation of claims made by this resume. Claims are supported by " +
        "human-readable prose or external evidence. Absence of a claim does not imply absence of a skill or capability.";

    [YamlMember(Alias = "semantics", Order = 3)]
    public List<string> Semantics { get; set; } = [.. DefaultSemantics];
}

public sealed class JobMlDocumentMetadata
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "resume";

    [YamlMember(Alias = "language")]
    public string Language { get; set; } = "en-GB";

    /// <summary>
    /// Optional published endpoint for the full-resolution JobML projection.
    /// This is not the canonical career transcript, which can also contain source
    /// documents, editorial decisions, embeddings, centroids, and private analysis.
    /// </summary>
    [YamlMember(Alias = "full_jobml")]
    public string? FullJobMl { get; set; }

    /// <summary>JobML 0.1 draft compatibility alias. New documents emit full_jobml.</summary>
    [YamlMember(Alias = "complete_ledger")]
    public string? LegacyCompleteLedger { get; set; }

    [YamlIgnore]
    public string? EffectiveFullJobMl => FullJobMl ?? LegacyCompleteLedger;
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

    /// <summary>Distinguishes narrative claims from identity and semantic-index records.</summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

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
    /// <summary>Stable evidence identity in the full-resolution projection.</summary>
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "prose";

    [YamlMember(Alias = "ref")]
    public string? Ref { get; set; }

    [YamlMember(Alias = "uri")]
    public string? Uri { get; set; }

    /// <summary>Optional link to an entry in the career-record source catalogue.</summary>
    [YamlMember(Alias = "source_id")]
    public string? SourceId { get; set; }

    [YamlMember(Alias = "issuer")]
    public string? Issuer { get; set; }

    [YamlMember(Alias = "qualification")]
    public string? Qualification { get; set; }

    /// <summary>Bibliographic metadata used by the compact scientific-style projection.</summary>
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }

    [YamlMember(Alias = "authors")]
    public List<string> Authors { get; set; } = [];

    [YamlMember(Alias = "publisher")]
    public string? Publisher { get; set; }

    [YamlMember(Alias = "published")]
    public string? Published { get; set; }

    [YamlMember(Alias = "accessed")]
    public string? Accessed { get; set; }

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

    /// <summary>Optional derived semantic representation. It is not evidence.</summary>
    [YamlMember(Alias = "embedding")]
    public JobMlEmbedding? Embedding { get; set; }
}

public sealed class JobMlSource
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "resume";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "uri")]
    public string? Uri { get; set; }

    [YamlMember(Alias = "summary")]
    public string? Summary { get; set; }

    [YamlMember(Alias = "fingerprint")]
    public string? Fingerprint { get; set; }

    [YamlMember(Alias = "observed_at")]
    public string? ObservedAt { get; set; }
}

public sealed class JobMlSemanticSpace
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "model")]
    public string Model { get; set; } = "";

    [YamlMember(Alias = "dimensions")]
    public int Dimensions { get; set; }

    [YamlMember(Alias = "normalization")]
    public string Normalization { get; set; } = "l2";

    [YamlMember(Alias = "model_digest")]
    public string? ModelDigest { get; set; }
}

public sealed class JobMlEmbedding
{
    [YamlMember(Alias = "space")]
    public string Space { get; set; } = "";

    [YamlMember(Alias = "vector")]
    public List<float> Vector { get; set; } = [];
}

public sealed class JobMlRoleCentroid
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "space")]
    public string Space { get; set; } = "";

    [YamlMember(Alias = "derived_from")]
    public List<string> DerivedFrom { get; set; } = [];

    [YamlMember(Alias = "vector")]
    public List<float> Vector { get; set; } = [];
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
