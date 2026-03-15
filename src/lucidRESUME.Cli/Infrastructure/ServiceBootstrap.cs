using lucidRESUME.Export;
using lucidRESUME.Extraction;
using lucidRESUME.Ingestion;
using lucidRESUME.JobSearch;
using lucidRESUME.JobSpec;
using lucidRESUME.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Cli.Infrastructure;

public static class ServiceBootstrap
{
    public static IServiceProvider Build(string? configFile = null)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(configFile ?? "lucidresume.json", optional: true)
            .AddEnvironmentVariables("LUCIDRESUME_")
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(l => l
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec();
        services.AddMatching();
        services.AddExport();
        services.AddJobSearch(config);

        return services.BuildServiceProvider();
    }
}
