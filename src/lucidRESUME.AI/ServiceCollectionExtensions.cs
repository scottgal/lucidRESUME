using lucidRESUME.Core.Interfaces;
using lucidRESUME.Compiler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiTailoring(this IServiceCollection services, IConfiguration config)
    {
        // Configure all provider options
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        services.Configure<AnthropicOptions>(config.GetSection("Anthropic"));
        services.Configure<OpenAiOptions>(config.GetSection("OpenAi"));
        services.Configure<LlamaSharpOptions>(config.GetSection("LlamaSharp"));
        services.Configure<TailoringOptions>(config.GetSection("Tailoring"));
        services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));
        services.Configure<JevOptions>(config.GetSection("Jev"));

        if (config.GetSection("Jev").GetValue<bool>("Enabled"))
        {
            services.AddHttpClient<IResumeDecisionProvider, JevResumeDecisionProvider>(client =>
                client.Timeout = TimeSpan.FromSeconds(30));
        }

        // Full-strength OpenAI is the primary configured provider. Local LLamaSharp
        // remains available as an explicitly selected experimental/offline path.
        var tailoringProvider = config.GetSection("Tailoring").GetValue<string>("Provider") ?? "openai";
        var extractionProvider = config.GetSection("Tailoring").GetValue<string>("ExtractionProvider")
                                 ?? tailoringProvider;

        services.AddHttpClient<LlamaSharpModelManager>(client =>
            client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<LlamaSharpRuntime>();
        services.AddSingleton<IResumeCompositionProvider, LlamaSharpResumeCompositionProvider>();
        services.AddHttpClient<IResumeCompositionProvider, OpenAiResumeCompositionProvider>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
            });

        switch (tailoringProvider.ToLowerInvariant())
        {
            case "llamasharp":
                services.AddSingleton<IAiTailoringService, LlamaSharpTailoringService>();
                break;
            case "anthropic":
                services.AddHttpClient<IAiTailoringService, AnthropicTailoringService>()
                    .AddStandardResilienceHandler();
                break;
            case "openai":
                services.AddHttpClient<IAiTailoringService, OpenAiTailoringService>()
                    .AddStandardResilienceHandler(options =>
                    {
                        // Full evidence catalogues and complete resume outputs routinely
                        // take longer than the standard handler's 10 second attempt limit.
                        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                    });
                break;
            default: // ollama
                services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
                    .AddStandardResilienceHandler();
                break;
        }

        switch (extractionProvider.ToLowerInvariant())
        {
            case "none":
            case "disabled":
                // Deterministic-only ingestion is a supported operating mode.
                // Do not silently fall back to a network or local model.
                break;
            case "llamasharp":
                services.AddSingleton<ILlmExtractionService, LlamaSharpExtractionService>();
                break;
            case "anthropic":
                services.AddHttpClient<ILlmExtractionService, AnthropicExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
            case "openai":
                services.AddHttpClient<ILlmExtractionService, OpenAiExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
            case "ollama":
                services.AddHttpClient<ILlmExtractionService, OllamaExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown Tailoring:ExtractionProvider '{extractionProvider}'. " +
                    "Use none, llamasharp, ollama, openai, or anthropic.");
        }

        // Model discovery for settings UI
        services.AddHttpClient<ModelDiscoveryService>();

        // Embedding indexer + deterministic semantic compression
        services.AddSingleton<EmbeddingIndexer>();
        services.AddSingleton<SemanticCompressor>();

        // Embedding: ONNX by default (fully local), Ollama if explicitly configured
        var embeddingProvider = config.GetSection("Embedding").GetValue<string>("Provider") ?? "onnx";
        if (embeddingProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>()
                .AddStandardResilienceHandler();
        }
        else
        {
            services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
        }

        return services;
    }
}
