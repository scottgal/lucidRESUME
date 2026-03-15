using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Matching;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMatching(this IServiceCollection services)
    {
        services.AddSingleton<IMatchingService, SkillMatchingService>();
        return services;
    }
}
