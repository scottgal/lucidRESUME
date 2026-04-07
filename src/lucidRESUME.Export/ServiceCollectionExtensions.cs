using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Export;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExport(this IServiceCollection services)
    {
        services.AddSingleton<IResumeExporter, JsonResumeExporter>();
        services.AddSingleton<IResumeExporter, MarkdownExporter>();
        services.AddSingleton<IResumeExporter, DocxExporter>();
        return services;
    }
}
