using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Matching;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMatching(this IServiceCollection services, IConfiguration? config = null)
    {
        if (config is not null)
        {
            services.Configure<CoverageOptions>(config.GetSection("Coverage"));
            services.Configure<CompanyClassifierOptions>(config.GetSection("CompanyClassifier"));
        }
        else
        {
            services.AddOptions<CoverageOptions>();
            services.AddOptions<CompanyClassifierOptions>();
        }

        services.AddSingleton<CompanyClassifier>();
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
        services.AddSingleton<ICoverageAnalyser>(sp =>
            new ResumeCoverageAnalyser(
                sp.GetRequiredService<CompanyClassifier>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CoverageOptions>>(),
                sp.GetService<IEmbeddingService>()));

        services.AddSingleton<QualitySynthesizer>();

        // Skill ledger builders + matcher + career planner
        services.AddSingleton<SkillLedgerBuilder>();
        services.AddSingleton<JdSkillLedgerBuilder>();
        services.AddSingleton<SkillLedgerMatcher>();
        services.AddSingleton<Graph.CareerPlanner>();
        services.AddSingleton<Graph.SearchQueryGenerator>();
        services.AddSingleton<Graph.AdaptiveQueryWidener>();
        services.AddSingleton<TaxonomyLearner>();
        services.AddSingleton<SkillTaxonomyService>();
        services.AddSingleton<ISkillTaxonomy>(sp => sp.GetRequiredService<SkillTaxonomyService>());

        return services;
    }
}
