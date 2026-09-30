using lucidRESUME.Compiler;

namespace lucidRESUME.Web;

/// <summary>
/// An immutable, role-specific resume projection published at an opaque URL.
/// The projection is compiled once; reads never invoke extraction, matching, or an LLM.
/// </summary>
public sealed record ResumePublication(
    string PublicId,
    DateTimeOffset PublishedAt,
    string? ApplicationReference,
    CompilationResult Compilation,
    int MinimumPages = 2);

public interface IResumePublicationStore
{
    string CreatePublicId();

    Task PublishAsync(ResumePublication publication, CancellationToken cancellationToken = default);

    Task<ResumePublication?> GetAsync(string publicId, CancellationToken cancellationToken = default);
}

public interface IResumeMarkdownRenderer
{
    string ToHtml(string markdown);
    string ToTranscriptHtml(string markdown);
}

public sealed record CompileRequest(string JobDescription, string? SourceRevision = null,
    bool Polish = true, string? Provider = "openai", bool Publish = true,
    string? ApplicationReference = null, bool IncludeCitations = true, int MinimumPages = 2);
