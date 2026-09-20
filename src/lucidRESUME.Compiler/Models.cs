using lucidRESUME.JobML;

namespace lucidRESUME.Compiler;

public sealed record JobMlSnapshot(
    string Revision,
    DateTimeOffset PublishedAt,
    string Source,
    JobMlFile File,
    IReadOnlyList<JobMlDiagnostic> Diagnostics);

public enum RequirementKind { Required, Preferred, Responsibility }
public enum MatchKind { Direct, Related, None }

public sealed record CompilerRequirement(string Id, string Text, RequirementKind Kind, string SourceText);

public sealed record ClaimMatch(
    string RequirementId,
    string ClaimId,
    MatchKind Kind,
    double Score,
    string Reason);

public sealed record SelectedClaim(
    JobMlClaim Claim,
    string SubjectName,
    string Prose,
    IReadOnlyList<string> EvidenceIds,
    double Score,
    IReadOnlyList<ClaimMatch> Matches);

public sealed record EvidencePacket(
    string SectionId,
    string Heading,
    string Intent,
    int MaximumWords,
    IReadOnlyList<SelectedClaim> Claims,
    IReadOnlyList<string> RequirementIds);

public sealed record ProjectionManifest(
    string SourceRevision,
    string JobDescriptionRevision,
    DateTimeOffset CompiledAt,
    IReadOnlyList<CompilerRequirement> Requirements,
    IReadOnlyList<EvidencePacket> Sections,
    IReadOnlyList<ClaimMatch> Matches,
    IReadOnlyList<string> Gaps,
    string EmbeddingProvider);

public sealed record CompositionBlock(
    string SectionId,
    string Text,
    IReadOnlyList<string> ClaimIds,
    IReadOnlyList<string> EvidenceIds);

public sealed record CompositionDraft(
    IReadOnlyList<CompositionBlock> Blocks,
    IReadOnlyList<string> Warnings);

public enum CompositionPass { Tighten, HumanVoice }

public sealed record CompositionPassRequest(
    CompositionPass Pass,
    string JobDescription,
    ProjectionManifest Manifest,
    IReadOnlyList<CompositionBlock> SourceBlocks,
    IReadOnlyList<CompositionBlock> CurrentBlocks);

public interface IResumeCompositionProvider
{
    string ProviderId { get; }
    bool IsAvailable { get; }
    Task<CompositionDraft> RunPassAsync(
        CompositionPassRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CompilationOptions
{
    public int MaximumClaims { get; set; } = 18;
    public int MaximumClaimsPerSubject { get; set; } = 5;
    public double RelatedThreshold { get; set; } = 0.56;
    public double DiversityPenalty { get; set; } = 0.18;
    public bool ComposeProse { get; set; }
    public string? CompositionProvider { get; set; }
    public string? CompleteLedgerUri { get; set; }
}

public sealed record CompilationResult(
    string CompilationId,
    ProjectionManifest Manifest,
    string HumanMarkdown,
    string PublishedMarkdown,
    string FullJobMlMarkdown,
    JobMlFile ProjectedJobMl,
    bool UsedCompositionProvider,
    string? CompositionProvider,
    IReadOnlyList<string> Warnings);

public sealed class JobMlPublicationException(string message) : Exception(message);
