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

public sealed class AnthropicTailoringService : IAiTailoringService
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly TailoringOptions _tailoringOptions;
    private readonly ILogger<AnthropicTailoringService> _logger;
    private readonly ITermNormalizer _termNormalizer;
    private readonly ICoverageAnalyser _coverageAnalyser;

    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public AnthropicTailoringService(HttpClient http, IOptions<AnthropicOptions> options,
        IOptions<TailoringOptions> tailoringOptions,
        ILogger<AnthropicTailoringService> logger, ITermNormalizer termNormalizer,
        ICoverageAnalyser coverageAnalyser)
    {
        _http = http;
        _options = options.Value;
        _tailoringOptions = tailoringOptions.Value;
        _logger = logger;
        _termNormalizer = termNormalizer;
        _coverageAnalyser = coverageAnalyser;

        _http.BaseAddress = new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        var (termMappings, coverage) = await PrepareContextAsync(resume, job, ct);

        var prompt = TailoringPromptBuilder.Build(resume, job, profile, termMappings, coverage, _tailoringOptions);

        _logger.LogInformation("Tailoring resume for {Title} at {Company} using Anthropic {Model}",
            job.Title, job.Company, _options.Model);

        var request = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await _http.PostAsJsonAsync("v1/messages", request, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicMessagesResponse>(cancellationToken: cts.Token);
        var tailoredMarkdown = result?.Content?.FirstOrDefault()?.Text?.Trim() ?? resume.RawMarkdown ?? "";

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
            var request = new { model = _options.Model, max_tokens = 1,
                messages = new[] { new { role = "user", content = "ping" } } };
            var response = await _http.PostAsJsonAsync("v1/messages", request, ct);
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

    private record AnthropicMessagesResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);
    private record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
