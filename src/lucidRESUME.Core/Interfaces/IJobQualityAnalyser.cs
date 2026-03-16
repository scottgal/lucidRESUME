using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Quality;

namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Scores the quality and completeness of a job description.
/// Higher scores = more complete JD = better signal for matching and tailoring.
/// </summary>
public interface IJobQualityAnalyser
{
    QualityReport Analyse(JobDescription job);
}
