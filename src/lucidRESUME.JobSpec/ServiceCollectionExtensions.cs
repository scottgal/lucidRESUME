using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.JobSpec;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobSpec(this IServiceCollection services)
    {
        services.AddHttpClient<IJobSpecParser, JobSpecParser>()
            .AddStandardResilienceHandler();
        return services;
    }
}
