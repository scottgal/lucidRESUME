using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class LlamaSharpTailoringService : IAiTailoringService
{
    private readonly LlamaSharpRuntime _runtime;
    private readonly LlamaSharpOptions _options;
    private readonly TailoringOptions _tailoringOptions;
    private readonly ILogger<LlamaSharpTailoringService> _logger;
    private readonly ITermNormalizer _termNormalizer;
    private readonly ICoverageAnalyser _coverageAnalyser;

    public bool IsAvailable => _runtime.IsAvailable;

    public LlamaSharpTailoringService(
        LlamaSharpRuntime runtime,
        IOptions<LlamaSharpOptions> options,
        IOptions<TailoringOptions> tailoringOptions,
        ILogger<LlamaSharpTailoringService> logger,
        ITermNormalizer termNormalizer,
        ICoverageAnalyser coverageAnalyser)
    {
        _runtime = runtime;
        _options = options.Value;
        _tailoringOptions = tailoringOptions.Value;
        _logger = logger;
        _termNormalizer = termNormalizer;
        _coverageAnalyser = coverageAnalyser;
    }

    public async Task<ResumeDocument> TailorAsync(
        ResumeDocument resume,
        JobDescription job,
        UserProfile profile,
        CancellationToken ct = default)
    {
        IReadOnlyList<TermMatch>? termMappings = null;
        var resumeTerms = resume.Skills.Select(s => s.Name)
            .Concat(resume.Experience.SelectMany(e => e.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var jdTerms = job.RequiredSkills.Concat(job.PreferredSkills)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (resumeTerms.Count > 0 && jdTerms.Count > 0)
        {
            try
            {
                termMappings = await _termNormalizer.FindMatchesAsync(
                    jdTerms,
                    resumeTerms,
                    _tailoringOptions.TermNormalizationMinSimilarity,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Term normalization failed; continuing without it");
            }
        }

        CoverageReport? coverage = null;
        try
        {
            coverage = await _coverageAnalyser.AnalyseAsync(resume, job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Coverage analysis failed; continuing without it");
        }

        var prompt = TailoringPromptBuilder.Build(
            resume,
            job,
            profile,
            termMappings,
            coverage,
            _tailoringOptions);
        _logger.LogInformation("Tailoring resume for {Title} at {Company} using {Model}",
            job.Title, job.Company, _options.ModelId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var markdown = await _runtime.GenerateAsync(
            prompt,
            "You are an exacting resume editor. Return only the requested Markdown. " +
            "Never invent experience, skills, dates, metrics, or evidence. Do not expose hidden reasoning.",
            _options.MaxTokens,
            timeout.Token);

        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(markdown, null, null);
        tailored.MarkTailoredFor(job.JobId);
        foreach (var entity in resume.Entities)
            tailored.AddEntity(entity);
        return tailored;
    }
}
