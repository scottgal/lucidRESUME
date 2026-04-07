using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mostlylucid.DocSummarizer.Extensions;

namespace lucidRESUME.GitHub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitHub(this IServiceCollection services, IConfiguration? config = null)
    {
        if (config is not null)
            services.Configure<GitHubImportOptions>(config.GetSection("GitHub"));
        else
            services.AddOptions<GitHubImportOptions>();

        services.AddHttpClient<GitHubApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("lucidRESUME", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        // Register lucidRAG DocSummarizer for README analysis (BERT mode, no LLM needed)
        services.AddDocSummarizer();

        services.AddSingleton<GitHubSkillImporter>();

        return services;
    }
}
