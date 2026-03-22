using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling;
using lucidRESUME.Ingestion.Images;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Ingestion;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration config)
    {
        var doclingSection = config.GetSection("Docling");
        services.Configure<DoclingOptions>(doclingSection);

        if (doclingSection.GetValue<bool>("Enabled"))
        {
            services.AddHttpClient<IDoclingClient, DoclingClient>()
                .AddStandardResilienceHandler();
        }

        services.AddSingleton<IDocumentImageCache>(_ => new FileSystemImageCache());
        services.AddDirectParsing();
        services.AddTransient<IResumeParser, ResumeParser>();
        return services;
    }
}


