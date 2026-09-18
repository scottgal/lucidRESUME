using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion;
using Microsoft.Extensions.DependencyInjection;

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
            return corpus.Merged;
        }

        if (file is null)
            throw new ArgumentException("Provide either --resume FILE or --resume-dir DIRECTORY.");
        if (!file.Exists)
            throw new FileNotFoundException("Resume file not found.", file.FullName);

        Console.Error.WriteLine($"Parsing {file.Name}...");
        return await ParseHelper.ParseAndAwaitAsync(
            services.GetRequiredService<IResumeParser>(), file.FullName, ct);
    }
}
