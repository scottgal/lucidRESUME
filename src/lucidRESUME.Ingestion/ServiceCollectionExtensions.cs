using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling;
using lucidRESUME.Ingestion.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Ingestion;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DoclingOptions>(config.GetSection("Docling"));
        services.AddHttpClient<IDoclingClient, DoclingClient>()
            .AddStandardResilienceHandler();
        services.AddScoped<IResumeParser, ResumeParser>();
        return services;
    }
}
