using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface ICareerProseEnricher
{
    Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false);
}

public sealed record ProseSearchHit(string SegmentId, double Score);

public interface IProseSearch
{
    Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default);
}
