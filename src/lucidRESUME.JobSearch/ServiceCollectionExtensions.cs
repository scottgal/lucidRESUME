using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobSearch.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.JobSearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobSearch(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AdzunaOptions>(config.GetSection("Adzuna"));
        services.AddHttpClient<RemotiveAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<AdzunaAdapter>().AddStandardResilienceHandler();
        services.AddSingleton<IJobSearchAdapter, RemotiveAdapter>();
        services.AddSingleton<IJobSearchAdapter, AdzunaAdapter>();
        services.AddSingleton<JobSearchService>();
        return services;
    }
}
