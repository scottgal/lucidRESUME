using lucidRESUME.Core.Interfaces;
using lucidRESUME.Ingestion.Docling;
using lucidRESUME.Ingestion.Images;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.Ingestion.LinkedIn;
using lucidRESUME.Ingestion.Layout;
using lucidRESUME.Ingestion.Preview;
using lucidRESUME.Ingestion.Web;
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
        services.AddSingleton<MorphDocxPreviewService>();
        services.AddHttpClient<DocumentLayoutDetector>();
        services.AddHttpClient<LinkedPostImporter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lucidRESUME/2.0 linked-evidence-importer");
        }).AddStandardResilienceHandler();
        services.AddTransient<LinkedInZipParser>();
        services.AddDirectParsing();
        services.AddTransient<IResumeParser, ResumeParser>();
        services.AddTransient<ResumeCorpusLoader>();
        return services;
    }
}
