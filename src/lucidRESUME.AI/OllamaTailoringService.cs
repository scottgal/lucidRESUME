using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class OllamaTailoringService : IAiTailoringService
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly TailoringOptions _tailoringOptions;
    private readonly ILogger<OllamaTailoringService> _logger;
    private readonly ITermNormalizer _termNormalizer;
    private readonly ICoverageAnalyser _coverageAnalyser;

    // volatile: written by CheckAvailabilityAsync (potentially background thread),
    // read by UI thread - ensures visibility without locking.
    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public OllamaTailoringService(HttpClient http, IOptions<OllamaOptions> options,
        IOptions<TailoringOptions> tailoringOptions,
        ILogger<OllamaTailoringService> logger, ITermNormalizer termNormalizer,
        ICoverageAnalyser coverageAnalyser)
    {
        _http = http;
        _options = options.Value;
        _tailoringOptions = tailoringOptions.Value;
        _logger = logger;
        _termNormalizer = termNormalizer;
        _coverageAnalyser = coverageAnalyser;
    }

    public async Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        IReadOnlyList<TermMatch>? termMappings = null;

        var resumeTerms = resume.Skills
            .Select(s => s.Name)
            .Concat(resume.Experience.SelectMany(e => e.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var jdTerms = job.RequiredSkills
            .Concat(job.PreferredSkills)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (resumeTerms.Count > 0 && jdTerms.Count > 0)
        {
            try
            {
                termMappings = await _termNormalizer.FindMatchesAsync(jdTerms, resumeTerms,
                    minSimilarity: _tailoringOptions.TermNormalizationMinSimilarity, ct: ct);
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

        var prompt = TailoringPromptBuilder.Build(resume, job, profile, termMappings, coverage, _tailoringOptions);

        _logger.LogInformation("Tailoring resume for {Title} at {Company} using {Model} (ctx={NumCtx})",
            job.Title, job.Company, _options.Model, _options.NumCtx);

        // stream=true avoids buffering long think tokens before any output arrives.
        // think=false disables Qwen3 chain-of-thought - we want the final answer directly.
        // num_ctx is tuned in appsettings to fit resume + JD without truncation.
        var request = new
        {
            model = _options.Model,
            prompt,
            stream = true,
            think = false,
            options = new { num_ctx = _options.NumCtx }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await _http.PostAsJsonAsync($"{_options.BaseUrl}/api/generate", request, cts.Token);
        response.EnsureSuccessStatusCode();

        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            if (chunk is null) continue;
            if (!string.IsNullOrEmpty(chunk.Response))
                sb.Append(chunk.Response);
            if (chunk.Done) break;
        }

        var tailoredMarkdown = sb.Length > 0 ? sb.ToString().Trim() : resume.RawMarkdown ?? "";

        // Create a new tailored document - original is preserved
        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(tailoredMarkdown, null, null);
        tailored.MarkTailoredFor(job.JobId);

        // Copy entity metadata - structured fields stay from original extraction
        foreach (var entity in resume.Entities)
            tailored.AddEntity(entity);

        return tailored;
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_options.BaseUrl}/api/tags", ct);
            _isAvailable = response.IsSuccessStatusCode;
            return _isAvailable;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }

    private record OllamaStreamChunk(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}