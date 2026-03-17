using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface ICoverageAnalyser
{
    /// <summary>
    /// Maps each requirement in <paramref name="job"/> to the best-matching
    /// evidence in <paramref name="resume"/>. Gaps are entries with null Evidence.
    /// </summary>
    Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default);
}
