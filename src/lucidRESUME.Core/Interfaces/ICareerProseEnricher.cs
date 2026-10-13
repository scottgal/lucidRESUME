using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface ICareerProseEnricher
{
    Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false);
}

public sealed record ProseSearchHit(string SegmentId, double Score);

public sealed record ProseSearchQuery(string Revision, string Text, string? SubjectId = null, string? Kind = null,
    float[]? Embedding = null, string? EmbeddingSpace = null, int Maximum = 64);

public interface IProseSearch
{
    Task PrepareAsync(CareerProseIndex index, CancellationToken ct = default) => Task.CompletedTask;
    Task<IReadOnlyList<ProseSearchHit>> SearchAsync(ProseSearchQuery query, CancellationToken ct = default)
        => SearchAsync(query.Revision, query.Text, query.Maximum, ct);
    Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default);
}
