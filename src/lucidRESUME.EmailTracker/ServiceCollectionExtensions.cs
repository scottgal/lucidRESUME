using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.EmailTracker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEmailTracker(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EmailScannerOptions>(config.GetSection("Email"));
        services.AddSingleton<IEmailScanner, ImapEmailScanner>();
        services.AddSingleton<EmailScanOrchestrator>();
        return services;
    }
}
