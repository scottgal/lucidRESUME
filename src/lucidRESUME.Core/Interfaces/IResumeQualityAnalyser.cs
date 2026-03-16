using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Quality;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Analyses resume quality and optionally scores alignment against a job description.
/// </summary>
public interface IResumeQualityAnalyser
{
    /// <summary>Standalone quality report — no JD alignment.</summary>
    QualityReport Analyse(ResumeDocument resume);

    /// <summary>Quality report with keyword-based JD alignment.</summary>
    QualityReport Analyse(ResumeDocument resume, JobDescription job);

    /// <summary>Async standalone — uses semantic embeddings for better analysis when available.</summary>
    Task<QualityReport> AnalyseAsync(ResumeDocument resume, CancellationToken ct = default);

    /// <summary>Async with JD — uses semantic embeddings for alignment scoring when available.</summary>
    Task<QualityReport> AnalyseAsync(ResumeDocument resume, JobDescription job, CancellationToken ct = default);
}
