using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class LlamaSharpExtractionService : ILlmExtractionService
{
    private const string SystemMessage =
        "You extract literal facts from resumes and job descriptions. Follow the requested output format exactly. " +
        "Never infer unsupported facts and never expose hidden reasoning.";

    private readonly LlamaSharpRuntime _runtime;
    private readonly LlamaSharpOptions _options;
    private readonly ILogger<LlamaSharpExtractionService> _logger;

    public bool IsAvailable => _runtime.IsAvailable;

    public LlamaSharpExtractionService(
        LlamaSharpRuntime runtime,
        IOptions<LlamaSharpOptions> options,
        ILogger<LlamaSharpExtractionService> logger)
    {
        _runtime = runtime;
        _options = options.Value;
        _logger = logger;
    }

    public Task<string?> ExtractSkillsAsync(string text, CancellationToken ct = default)
    {
        var input = text.Length > 6_000 ? text[..6_000] : text;
        return CallAsync(
            "List every explicitly mentioned technical skill (languages, frameworks, tools, and databases). " +
            "Reply with only a comma-separated list. Do not add related skills.\n\n" + input,
            1_000,
            ct);
    }

    public Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default)
    {
        var input = text.Length > 10_000 ? text[..10_000] : text;
        return CallAsync($"""
            Extract only work experience entries. Return one job per line in this exact format:
            COMPANY_NAME | JOB_TITLE | START_DATE - END_DATE
            Use "Unknown" for a missing company or date. Include no bullets, achievements, education, or explanation.

            {input}
            """, 2_000, ct);
    }

    public Task<string?> ExtractJsonAsync(string prompt, CancellationToken ct = default) =>
        CallAsync(prompt, 4_000, ct);

    public async Task<string?> ExtractNameAsync(string headerText, CancellationToken ct = default)
    {
        var input = headerText.Length > 800 ? headerText[..800] : headerText;
        var result = await CallAsync(
            "Return only the person's full name from this resume header, or UNKNOWN if it is not explicit.\n\n" + input,
            100,
            ct);
        return string.IsNullOrWhiteSpace(result) || result.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? null
            : result.Trim('"', '\'', '.', ' ', '\n', '\r');
    }

    private async Task<string?> CallAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        if (!IsAvailable)
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var result = await _runtime.GenerateAsync(prompt, SystemMessage, maxTokens, timeout.Token);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "LLamaSharp extraction failed with {Model}", _options.ModelId);
            return null;
        }
    }
}
