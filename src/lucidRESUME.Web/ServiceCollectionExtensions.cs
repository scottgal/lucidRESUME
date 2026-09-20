using lucidRESUME.AI;
using lucidRESUME.Compiler;
using lucidRESUME.Export;
using lucidRESUME.JobSpec;
using lucidRESUME.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds the prebuilt complete-resume to targeted-resume compiler.</summary>
    public static IServiceCollection AddLucidResumeCompiler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddJobSpec(configuration);
        services.AddMatching(configuration);
        services.AddAiTailoring(configuration);
        services.AddJobMlCompiler(configuration);
        services.AddExport();
        services.AddSingleton<CompilationSessionStore>();
        return services;
    }
}
