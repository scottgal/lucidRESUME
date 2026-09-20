using lucidRESUME.Parsing.Templates;
using lucidRESUME.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Parsing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDirectParsing(this IServiceCollection services)
    {
        services.AddSingleton<TemplateRegistry>(sp =>
            new TemplateRegistry(AppDataPaths.Resolve("templates.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TemplateRegistry>>()));

        // DocxDirectParser gets the registry injected so matched templates boost confidence
        services.AddSingleton<IDocumentParser>(sp =>
            new DocxDirectParser(sp.GetRequiredService<TemplateRegistry>()));

        services.AddSingleton<IDocumentParser, PdfTextParser>();
        services.AddSingleton<IDocumentParser, TxtParser>();
        services.AddSingleton<ParserSelector>();
        return services;
    }
}
