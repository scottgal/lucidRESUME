using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion;
using lucidRESUME.Matching;
using lucidRESUME.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Cli.Infrastructure;

internal static class ResumeInputHelper
{
    public static async Task<ResumeDocument> LoadAsync(
        IServiceProvider services,
        FileInfo? file,
        DirectoryInfo? directory,
        CancellationToken ct)
    {
        if (directory is not null)
        {
            Console.Error.WriteLine($"Loading resume corpus from {directory.FullName}...");
            var corpus = await services.GetRequiredService<ResumeCorpusLoader>()
                .LoadDirectoryAsync(directory.FullName, ct);
            foreach (var source in corpus.Sources)
                Console.Error.WriteLine($"  {source.FileName}: {source.Experience.Count} positions, {source.Skills.Count} skills, {source.Projects.Count} projects");
            Console.Error.WriteLine($"Structured merge sources: {string.Join(", ", corpus.StructuredSources)}");
            Console.Error.WriteLine($"Merged ledger: {corpus.Merged.Experience.Count} positions, {corpus.Merged.Skills.Count} skills, {corpus.Merged.Projects.Count} projects");
            foreach (var anomaly in corpus.Anomalies)
                Console.Error.WriteLine($"  REVIEW [{anomaly.Severity}] {anomaly.Description}");
            await CategoriseAsync(services, corpus.Merged, ct);
            return corpus.Merged;
        }

        if (file is null)
            throw new ArgumentException("Provide either --resume FILE or --resume-dir DIRECTORY.");
        if (!file.Exists)
            throw new FileNotFoundException("Resume file not found.", file.FullName);

        Console.Error.WriteLine($"Parsing {file.Name}...");
        var parsed = await ParseHelper.ParseAndAwaitAsync(
            services.GetRequiredService<IResumeParser>(), file.FullName, ct);
        await CategoriseAsync(services, parsed, ct);
        return parsed;
    }

    private static async Task CategoriseAsync(IServiceProvider services, ResumeDocument resume,
        CancellationToken ct)
    {
        SkillCategoriser.Categorise(resume, useDomainFallback: false);
        if (services.GetService<IResumeDecisionProvider>() is { } decisions)
        {
            var policy = services.GetRequiredService<IOptions<NimbleOptions>>().Value;
            await SkillCategoriser.CategoriseAmbiguousAsync(resume, decisions,
                policy.AcceptanceProbability, policy.MinimumMargin, ct);
        }
        SkillCategoriser.Categorise(resume);
    }
}
