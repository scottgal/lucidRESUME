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
        return CallAsync(ResumeExtractionPrompts.Skills(text), 1_000, ct);
    }

    public Task<string?> ExtractExperienceSummaryAsync(string text, CancellationToken ct = default)
    {
        return CallAsync(ResumeExtractionPrompts.Experience(text, 10_000), 2_000, ct);
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
            // Extraction is a bounded parsing operation. Greedy decoding keeps the same
            // document and prompt stable across repeated ledger builds; creative sampling
            // remains available to explicit authoring workflows.
            var result = await _runtime.GenerateAsync(
                prompt, SystemMessage, maxTokens, timeout.Token, temperature: 0f);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "LLamaSharp extraction failed with {Model}", _options.ModelId);
            return null;
        }
    }
}
