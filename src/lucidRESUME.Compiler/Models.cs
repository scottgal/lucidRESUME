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
    IReadOnlyList<string> RequirementIds,
    string Kind = "experience");

public sealed record ProjectionManifest(
    string SourceRevision,
    string JobDescriptionRevision,
    DateTimeOffset CompiledAt,
    IReadOnlyList<CompilerRequirement> Requirements,
    IReadOnlyList<EvidencePacket> Sections,
    IReadOnlyList<ClaimMatch> Matches,
    IReadOnlyList<string> Gaps,
    string EmbeddingProvider)
{
    public string? TargetTitle { get; init; }
}

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
    /// <summary>Two-page senior-career projection budget used unless a compact export is requested.</summary>
    public int MaximumClaims { get; set; } = 16;
    public int MaximumClaimsPerSubject { get; set; } = 3;
    public int MaximumSections { get; set; } = 8;
    public int MinimumExperienceSections { get; set; } = 6;
    public int MinimumProjectSections { get; set; } = 3;
    public double RelatedThreshold { get; set; } = 0.56;
    public double DiversityPenalty { get; set; } = 0.18;
    /// <summary>Include qualifying roles omitted from the detailed projection as a compact chronology.</summary>
    public bool IncludeAdditionalExperience { get; set; } = true;
    /// <summary>Roles must be strictly longer than this many calendar months to enter the compact chronology.</summary>
    public int MinimumAdditionalExperienceMonths { get; set; } = 3;
    public bool ComposeProse { get; set; }
    public string? CompositionProvider { get; set; }
    /// <summary>Published endpoint for the full JobML career-record projection.</summary>
    public string? FullJobMlUri { get; set; }
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
