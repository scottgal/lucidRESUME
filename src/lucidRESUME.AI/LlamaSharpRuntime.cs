using System.Text;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>Lazy, process-wide LLamaSharp runtime. Serialises inference so one model is loaded once.</summary>
public sealed partial class LlamaSharpRuntime : IDisposable
{
    private readonly LlamaSharpOptions _options;
    private readonly LlamaSharpModelManager _models;
    private readonly ILogger<LlamaSharpRuntime> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _parameters;

    public LlamaSharpRuntime(
        IOptions<LlamaSharpOptions> options,
        LlamaSharpModelManager models,
        ILogger<LlamaSharpRuntime> logger)
    {
        _options = options.Value;
        _models = models;
        _logger = logger;
    }

    public bool IsAvailable => _models.IsModelPresent;

    public async Task<string> GenerateAsync(
        string prompt,
        string systemMessage,
        int? maxTokens = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _inferenceGate.WaitAsync(ct);
        try
        {
            var executor = new StatelessExecutor(_weights!, _parameters!)
            {
                ApplyTemplate = true,
                SystemMessage = systemMessage
            };
            var inference = new InferenceParams
            {
                MaxTokens = maxTokens ?? _options.MaxTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = _options.Temperature,
                    TopP = 0.9f,
                    RepeatPenalty = 1.05f
                }
            };

            var output = new StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inference, ct))
                output.Append(token);

            return RemoveThinking(output.ToString()).Trim();
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_weights is not null)
            return;

        await _loadGate.WaitAsync(ct);
        try
        {
            if (_weights is not null)
                return;
            if (!_models.IsModelPresent)
                throw new FileNotFoundException(
                    $"Local model {_models.ModelId} is not installed. Download it from AI Provider settings.",
                    _models.ModelPath);

            _parameters = new ModelParams(_models.ModelPath)
            {
                ContextSize = _options.ContextSize,
                GpuLayerCount = _options.GpuLayerCount,
                UseMemorymap = true,
                FlashAttention = true
            };
            _weights = await LLamaWeights.LoadFromFileAsync(_parameters, ct);
            _logger.LogInformation(
                "Loaded {ModelId} with LLamaSharp (context={Context}, GPU layers={GpuLayers})",
                _models.ModelId,
                _options.ContextSize,
                _options.GpuLayerCount);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    internal static string RemoveThinking(string value)
    {
        var cleaned = ThinkBlockRegex().Replace(value, string.Empty);
        var unclosed = cleaned.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (unclosed >= 0)
            cleaned = cleaned[..unclosed];
        return cleaned.Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("<think>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();

    public void Dispose()
    {
        _weights?.Dispose();
        _loadGate.Dispose();
        _inferenceGate.Dispose();
    }
}
