using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiTailoring(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        services.Configure<TailoringOptions>(config.GetSection("Tailoring"));
        services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));

        // Tailoring & extraction still use Ollama (optional — graceful failure if not running)
        services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
            .AddStandardResilienceHandler();
        services.AddHttpClient<ILlmExtractionService, OllamaExtractionService>(client =>
            client.Timeout = TimeSpan.FromSeconds(60));

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
