using System.Net.Http.Json;
using lucidRESUME.Core.Interfaces;
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
    private readonly ILogger<OllamaTailoringService> _logger;

    // volatile: written by CheckAvailabilityAsync (potentially background thread),
    // read by UI thread — ensures visibility without locking.
    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;

    public OllamaTailoringService(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaTailoringService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        var prompt = TailoringPromptBuilder.Build(resume, job, profile);

        _logger.LogInformation("Tailoring resume for {Title} at {Company} using {Model}",
            job.Title, job.Company, _options.Model);

        var request = new { model = _options.Model, prompt, stream = false };
        var response = await _http.PostAsJsonAsync($"{_options.BaseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
        var tailoredMarkdown = result?.Response ?? resume.RawMarkdown ?? "";

        // Create a new tailored document — original is preserved
        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(tailoredMarkdown, null, null);
        tailored.MarkTailoredFor(job.JobId);

        // Copy entity metadata — structured fields stay from original extraction
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

    private record OllamaGenerateResponse(string Response);
}
