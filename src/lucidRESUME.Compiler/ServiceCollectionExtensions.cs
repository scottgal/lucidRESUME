using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Compiler;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobMlCompiler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobMlCompilerOptions>()
            .Bind(configuration.GetSection(JobMlCompilerOptions.SectionName))
            .Validate(options => options.MaximumUploadBytes > 0, "MaximumUploadBytes must be positive.")
            .Validate(options => options.MaximumJobDescriptionBytes > 0,
                "MaximumJobDescriptionBytes must be positive.")
            .Validate(options => options.CompilationCacheMinutes > 0,
                "CompilationCacheMinutes must be positive.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SnapshotDirectory),
                "SnapshotDirectory is required.")
            .ValidateOnStart();
        services.AddSingleton<IJobMlSnapshotStore, FileSystemJobMlSnapshotStore>();
        services.AddSingleton<CompositionValidator>();
        services.AddSingleton<ResumeCompositionOrchestrator>();
        services.AddTransient<IJobMlCompiler, JobMlResumeCompiler>();
        return services;
    }
}
