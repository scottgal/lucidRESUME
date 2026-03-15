using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobSearch.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.JobSearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobSearch(this IServiceCollection services, IConfiguration config)
    {
        // Key-auth adapters: options binding
        services.Configure<AdzunaOptions>(config.GetSection("Adzuna"));
        services.Configure<ReedOptions>(config.GetSection("Reed"));
        services.Configure<FindworkOptions>(config.GetSection("Findwork"));

        // HttpClient registrations
        services.AddHttpClient<RemotiveAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<AdzunaAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<ArbeitnowAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<JoinRiseAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<ReedAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<FindworkAdapter>().AddStandardResilienceHandler();
        services.AddHttpClient<JobicyRssAdapter>().AddStandardResilienceHandler();

        // Adapter registrations
        services.AddSingleton<IJobSearchAdapter, RemotiveAdapter>();
        services.AddSingleton<IJobSearchAdapter, AdzunaAdapter>();
        services.AddSingleton<IJobSearchAdapter, ArbeitnowAdapter>();
        services.AddSingleton<IJobSearchAdapter, JoinRiseAdapter>();
        services.AddSingleton<IJobSearchAdapter, JobicyRssAdapter>();
        services.AddSingleton<IJobSearchAdapter, ReedAdapter>();
        services.AddSingleton<IJobSearchAdapter, FindworkAdapter>();

        services.AddSingleton<JobSearchService>();
        return services;
    }
}
