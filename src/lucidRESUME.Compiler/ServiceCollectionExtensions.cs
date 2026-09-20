using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Compiler;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobMlCompiler(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobMlCompilerOptions>(configuration.GetSection(JobMlCompilerOptions.SectionName));
        services.AddSingleton<IJobMlSnapshotStore, FileSystemJobMlSnapshotStore>();
        services.AddSingleton<CompositionValidator>();
        services.AddSingleton<ResumeCompositionOrchestrator>();
        services.AddTransient<IJobMlCompiler, JobMlResumeCompiler>();
        return services;
    }
}
