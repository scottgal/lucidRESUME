using lucidRESUME.Core.Interfaces;
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
        services.Configure<TailoringOptions>(config.GetSection("Tailoring"));
        services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));

        // Provider selection: Tailoring.Provider (default: "ollama")
        var tailoringProvider = config.GetSection("Tailoring").GetValue<string>("Provider") ?? "ollama";

        switch (tailoringProvider.ToLowerInvariant())
        {
            case "anthropic":
                services.AddHttpClient<IAiTailoringService, AnthropicTailoringService>()
                    .AddStandardResilienceHandler();
                services.AddHttpClient<ILlmExtractionService, AnthropicExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
            case "openai":
                services.AddHttpClient<IAiTailoringService, OpenAiTailoringService>()
                    .AddStandardResilienceHandler();
                services.AddHttpClient<ILlmExtractionService, OpenAiExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
            default: // ollama
                services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
                    .AddStandardResilienceHandler();
                services.AddHttpClient<ILlmExtractionService, OllamaExtractionService>(client =>
                    client.Timeout = TimeSpan.FromSeconds(60));
                break;
        }

        // Model discovery for settings UI
        services.AddHttpClient<ModelDiscoveryService>();

        // Embedding indexer + semantic compressor + AI detection + de-AI rewriter + translator
        services.AddSingleton<EmbeddingIndexer>();
        services.AddSingleton<SemanticCompressor>();
        services.AddSingleton<OnnxAiTextDetector>();
        services.AddSingleton<AiDetectionScorer>();
        services.AddSingleton<DeAiRewriter>();
        services.AddSingleton<ResumeTranslator>();

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
