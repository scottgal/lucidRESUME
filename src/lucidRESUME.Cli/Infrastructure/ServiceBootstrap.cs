using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Persistence;
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
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(configFile ?? Path.Combine(Directory.GetCurrentDirectory(), "lucidresume.json"), optional: true)
            .AddEnvironmentVariables("LUCIDRESUME_")
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(l => l
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec(config);
        services.AddMatching(config);
        services.AddExport();
        services.AddJobSearch(config);
        services.AddAiTailoring(config);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "lucidRESUME");
        var dbPath = Path.Combine(appDataDir, "data.db");
        var jsonPath = Path.Combine(appDataDir, "data.json");
        services.AddSingleton<IAppStore>(_ => new SqliteAppStore(dbPath,
            jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null));

        return services.BuildServiceProvider();
    }
}
