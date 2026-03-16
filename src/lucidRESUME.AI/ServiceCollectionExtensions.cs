using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiTailoring(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
            .AddStandardResilienceHandler();
        services.AddHttpClient<ILlmExtractionService, OllamaExtractionService>(client =>
            client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>()
            .AddStandardResilienceHandler();
        return services;
    }
}
