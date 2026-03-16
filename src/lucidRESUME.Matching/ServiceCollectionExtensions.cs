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
        services.AddSingleton<IMatchingService>(sp =>
            new SkillMatchingService(
                sp.GetRequiredService<AspectExtractor>(),
                sp.GetService<IEmbeddingService>()));  // null if not registered
        services.AddSingleton<IResumeQualityAnalyser, ResumeQualityAnalyser>();
        services.AddSingleton<IJobQualityAnalyser, JobQualityAnalyser>();
        services.AddSingleton<ITermNormalizer, SemanticTermNormalizer>();
        return services;
    }
}
