using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class OpenAiTailoringService : IAiTailoringService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly TailoringOptions _tailoringOptions;
    private readonly ILogger<OpenAiTailoringService> _logger;
    private readonly ITermNormalizer _termNormalizer;
    private readonly ICoverageAnalyser _coverageAnalyser;

    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public OpenAiTailoringService(HttpClient http, IOptions<OpenAiOptions> options,
        IOptions<TailoringOptions> tailoringOptions,
        ILogger<OpenAiTailoringService> logger, ITermNormalizer termNormalizer,
        ICoverageAnalyser coverageAnalyser)
    {
        _http = http;
        _options = options.Value;
        _tailoringOptions = tailoringOptions.Value;
        _logger = logger;
        _termNormalizer = termNormalizer;
        _coverageAnalyser = coverageAnalyser;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
    }

    public async Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        var (termMappings, coverage) = await PrepareContextAsync(resume, job, ct);

        var prompt = TailoringPromptBuilder.Build(resume, job, profile, termMappings, coverage, _tailoringOptions);

        _logger.LogInformation("Tailoring resume for {Title} at {Company} using OpenAI {Model}",
            job.Title, job.Company, _options.Model);

        var request = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = new[]
            {
                new { role = "system", content = "You are a professional CV editor. Output tailored resume as clean Markdown only." },
                new { role = "user", content = prompt }
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await _http.PostAsJsonAsync("chat/completions", request, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cts.Token);
        var tailoredMarkdown = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
                               ?? resume.RawMarkdown ?? "";

        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(tailoredMarkdown, null, null);
        tailored.MarkTailoredFor(job.JobId);

        foreach (var entity in resume.Entities)
            tailored.AddEntity(entity);

        _isAvailable = true;
        return tailored;
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured) { _isAvailable = false; return false; }
        try
        {
            var response = await _http.GetAsync("models", ct);
            _isAvailable = response.IsSuccessStatusCode;
            return _isAvailable;
        }
        catch { _isAvailable = false; return false; }
    }

    private async Task<(IReadOnlyList<TermMatch>?, CoverageReport?)> PrepareContextAsync(
        ResumeDocument resume, JobDescription job, CancellationToken ct)
    {
        IReadOnlyList<TermMatch>? termMappings = null;
        var resumeTerms = resume.Skills.Select(s => s.Name)
            .Concat(resume.Experience.SelectMany(e => e.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var jdTerms = job.RequiredSkills.Concat(job.PreferredSkills)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (resumeTerms.Count > 0 && jdTerms.Count > 0)
        {
            try { termMappings = await _termNormalizer.FindMatchesAsync(jdTerms, resumeTerms,
                _tailoringOptions.TermNormalizationMinSimilarity, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Term normalization failed"); }
        }

        CoverageReport? coverage = null;
        try { coverage = await _coverageAnalyser.AnalyseAsync(resume, job, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Coverage analysis failed"); }

        return (termMappings, coverage);
    }

    private record OpenAiChatResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);
    private record Choice(
        [property: JsonPropertyName("message")] MessageContent? Message);
    private record MessageContent(
        [property: JsonPropertyName("content")] string? Content);
}
