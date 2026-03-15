using System.Net.Http.Json;
using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Ingestion.Docling;

public sealed class DoclingClient : IDoclingClient
{
    private readonly HttpClient _http;
    private readonly DoclingOptions _options;
    private readonly ILogger<DoclingClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DoclingClient(HttpClient http, IOptions<DoclingOptions> options, ILogger<DoclingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(new Uri(new Uri(_options.BaseUrl), "/health"), ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DoclingConversionResult> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        var contentType = GuessContentType(filePath);

        _logger.LogInformation("Submitting {FileName} to Docling at {BaseUrl}", fileName, _options.BaseUrl);

        await using var stream = File.OpenRead(filePath);
        var taskId = await SubmitAsync(stream, fileName, contentType, ct);

        _logger.LogInformation("Docling task {TaskId} submitted for {FileName}", taskId, fileName);

        await PollUntilCompleteAsync(taskId, ct);

        return await GetResultAsync(taskId, ct);
    }

    private async Task<string> SubmitAsync(Stream fileStream, string fileName, string contentType, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();

        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "files", fileName);
        content.Add(new StringContent("md"), "to_formats");
        content.Add(new StringContent("json"), "to_formats");
        content.Add(new StringContent("true"), "do_ocr");
        content.Add(new StringContent("true"), "do_table_structure");

        var baseUri = new Uri(_options.BaseUrl);
        var response = await _http.PostAsync(new Uri(baseUri, "/v1/convert/file/async"), content, ct);
        response.EnsureSuccessStatusCode();

        var submission = await response.Content.ReadFromJsonAsync<DoclingTaskSubmission>(JsonOptions, ct);
        if (submission?.TaskId is null)
            throw new DoclingConversionException("Docling returned null task ID");

        return submission.TaskId;
    }

    private async Task PollUntilCompleteAsync(string taskId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
        var baseUri = new Uri(_options.BaseUrl);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _http.GetAsync(new Uri(baseUri, $"/v1/status/poll/{taskId}?wait=5"), ct);
            response.EnsureSuccessStatusCode();

            var status = await response.Content.ReadFromJsonAsync<DoclingTaskStatus>(JsonOptions, ct);
            if (status is null)
                throw new DoclingConversionException($"Docling returned null status for task {taskId}");

            _logger.LogDebug("Docling task {TaskId}: {Status} (position: {Position})",
                taskId, status.TaskStatus, status.TaskPosition);

            if (status.TaskStatus is "success")
                return;

            if (status.TaskStatus is "failure")
                throw new DoclingConversionException($"Docling conversion failed for task {taskId}");

            await Task.Delay(_options.PollingIntervalMs, ct);
        }

        throw new TimeoutException($"Docling conversion timed out after {_options.TimeoutSeconds}s for task {taskId}");
    }

    private async Task<DoclingConversionResult> GetResultAsync(string taskId, CancellationToken ct)
    {
        var baseUri = new Uri(_options.BaseUrl);
        var response = await _http.GetAsync(new Uri(baseUri, $"/v1/result/{taskId}"), ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DoclingApiResult>(JsonOptions, ct);
        if (result is null)
            throw new DoclingConversionException($"Docling returned null result for task {taskId}");

        var markdown = result.Document.MdContent ?? "";
        var json = result.Document.JsonContent is not null
            ? JsonSerializer.Serialize(result.Document.JsonContent)
            : null;
        var plainText = result.Document.TextContent;

        return new DoclingConversionResult(markdown, json, plainText);
    }

    private static string GuessContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
}

public sealed class DoclingConversionException : Exception
{
    public DoclingConversionException(string message) : base(message) { }
    public DoclingConversionException(string message, Exception inner) : base(message, inner) { }
}
