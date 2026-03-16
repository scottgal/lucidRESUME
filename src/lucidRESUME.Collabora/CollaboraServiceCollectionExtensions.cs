using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using lucidRESUME.Collabora.DocumentOpeners;
using lucidRESUME.Collabora.Services;
using lucidRESUME.Collabora.Views;

namespace lucidRESUME.Collabora;

public static class CollaboraServiceCollectionExtensions
{
    public static IServiceCollection AddCollabora(this IServiceCollection services, IConfiguration config)
    {
        var options = new CollaboraOptions();
        config.GetSection("Collabora").Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<LibreOfficeService>();
        services.AddSingleton<DocumentOpenerService>();
        services.AddSingleton<CollaboraService>();
        services.AddTransient<CollaboraEditorControl>();

        return services;
    }
}
