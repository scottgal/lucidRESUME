using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Matching;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMatching(this IServiceCollection services)
    {
        services.AddSingleton<AspectExtractor>();
        services.AddSingleton<VoteService>();
        services.AddSingleton<JobFilterExecutor>();
        services.AddSingleton<IMatchingService, SkillMatchingService>();
        return services;
    }
}
