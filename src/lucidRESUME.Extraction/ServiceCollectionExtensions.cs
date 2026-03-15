using lucidRESUME.Core.Interfaces;
using lucidRESUME.Extraction.Ner;
using lucidRESUME.Extraction.Pipeline;
using lucidRESUME.Extraction.Recognizers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Extraction;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExtraction(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OnnxNerOptions>(config.GetSection("OnnxNer"));
        services.AddSingleton<IEntityDetector, ResumeRecognizerDetector>();
        services.AddSingleton<IEntityDetector, OnnxNerDetector>();
        services.AddSingleton<ExtractionPipeline>();
        return services;
    }
}
