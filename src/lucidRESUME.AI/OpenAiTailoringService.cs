using System.Net.Http.Json;
using System.Text.Json;
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
            max_output_tokens = _options.MaxTokens,
            instructions = "You are editing a resume from an evidence ledger. Never invent facts. Return JSON matching the supplied schema.",
            input = prompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "evidence_grounded_resume",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            markdown = new { type = "string" },
                            evidence_links = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        output_claim = new { type = "string" },
                                        evidence_refs = new { type = "array", items = new { type = "string" } }
                                    },
                                    required = new[] { "output_claim", "evidence_refs" },
                                    additionalProperties = false
                                }
                            },
                            warnings = new { type = "array", items = new { type = "string" } }
                        },
                        required = new[] { "markdown", "evidence_links", "warnings" },
                        additionalProperties = false
                    }
                }
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await _http.PostAsJsonAsync("responses", request, cts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        var outputText = ExtractOutputText(responseJson)
                         ?? throw new InvalidDataException("OpenAI returned no output text.");
        var result = JsonSerializer.Deserialize<OpenAiResumeResult>(outputText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidDataException("OpenAI returned an invalid structured resume response.");
        var tailoredMarkdown = result.Markdown.Trim();
        if (string.IsNullOrWhiteSpace(tailoredMarkdown))
            throw new InvalidDataException("OpenAI returned an empty resume.");

        if (result.Warnings.Count > 0)
            _logger.LogWarning("OpenAI resume generation warnings: {Warnings}", string.Join("; ", result.Warnings));
        var validReferences = TailoringPromptBuilder.EvidenceReferences(resume);
        var links = result.EvidenceLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.OutputClaim))
            .Select(link => new GenerationEvidenceLink
            {
                OutputClaim = link.OutputClaim.Trim(),
                EvidenceRefs = link.EvidenceRefs
                    .Where(validReferences.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(link => link.EvidenceRefs.Count > 0)
            .ToList();
        var rejectedReferenceCount = result.EvidenceLinks.Sum(link => link.EvidenceRefs.Count)
                                     - links.Sum(link => link.EvidenceRefs.Count);
        if (rejectedReferenceCount > 0)
            _logger.LogWarning("Rejected {Count} unknown evidence references returned by OpenAI", rejectedReferenceCount);
        _logger.LogInformation("OpenAI returned {LinkCount} grounded output claims using {EvidenceCount} supplied evidence references",
            links.Count, links.Sum(link => link.EvidenceRefs.Count));

        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(tailoredMarkdown, null, null);
        tailored.MarkTailoredFor(job.JobId);
        tailored.GenerationEvidenceLinks.AddRange(links);
        tailored.GenerationWarnings.AddRange(result.Warnings);

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

    internal static string? ExtractOutputText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();

        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                    return text.GetString();
            }
        }

        return null;
    }

    private sealed record OpenAiResumeResult(
        string Markdown,
        [property: System.Text.Json.Serialization.JsonPropertyName("evidence_links")] List<OpenAiEvidenceLink> EvidenceLinks,
        List<string> Warnings);

    private sealed record OpenAiEvidenceLink(
        [property: System.Text.Json.Serialization.JsonPropertyName("output_claim")] string OutputClaim,
        [property: System.Text.Json.Serialization.JsonPropertyName("evidence_refs")] List<string> EvidenceRefs);
}
