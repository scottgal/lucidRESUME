using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Parsing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDirectParsing(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentParser, DocxDirectParser>();
        services.AddSingleton<IDocumentParser, PdfTextParser>();
        services.AddSingleton<ParserSelector>();
        return services;
    }
}
