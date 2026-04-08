using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobSearch;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume search --query "senior .NET developer remote UK" [--output results.json]
/// lucidresume search --resume cv.docx [--prompt "roles matching my cloud skills"]
/// Decomposes a natural language prompt into job search queries across all 7 boards.
/// </summary>
public static class SearchCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var queryOpt = new Option<string?>("--query") { Description = "Search query (natural language)" };
        queryOpt.Aliases.Add("-q");
        var resumeOpt = new Option<FileInfo?>("--resume") { Description = "Resume file — auto-generates queries from your skills" };
        resumeOpt.Aliases.Add("-r");
        var promptOpt = new Option<string?>("--prompt") { Description = "Focus prompt for resume-based search (e.g. 'cloud roles in London')" };
        promptOpt.Aliases.Add("-p");
        var limitOpt = new Option<int?>("--limit") { Description = "Max results per adapter (default 10)" };
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("search", "Search jobs across 7 boards — from a query or auto-generated from your resume")
        {
            queryOpt, resumeOpt, promptOpt, limitOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var query = result.GetValue(queryOpt);
            var resumeFile = result.GetValue(resumeOpt);
            var prompt = result.GetValue(promptOpt);
            var limit = result.GetValue(limitOpt) ?? 10;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var searchService = sp.GetRequiredService<JobSearchService>();

            // If resume provided, generate search queries from skill communities
            if (resumeFile is { Exists: true })
            {
                var parser = sp.GetRequiredService<IResumeParser>();
                var ledgerBuilder = sp.GetRequiredService<Matching.SkillLedgerBuilder>();
                var queryGenerator = sp.GetRequiredService<Matching.Graph.SearchQueryGenerator>();

                Console.Error.WriteLine($"Parsing {resumeFile.Name}...");
                var resume = await ParseHelper.ParseAndAwaitAsync(parser, resumeFile.FullName, ct);
                var ledger = await ledgerBuilder.BuildAsync(resume, ct);

                var graph = new Matching.Graph.SkillGraph();
                graph.AddResumeLedger(ledger);
                graph.DetectCommunities();

                var suggestions = queryGenerator.Generate(graph, ledger);

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    // Filter suggestions by prompt keywords
                    var promptLower = prompt.ToLowerInvariant();
                    suggestions = suggestions
                        .Where(s => s.Query.ToLowerInvariant().Contains(promptLower)
                                    || promptLower.Split(' ').Any(w => s.Query.ToLowerInvariant().Contains(w)))
                        .ToList();
                }

                Console.Error.WriteLine($"Generated {suggestions.Count} search queries from skill communities:");
                foreach (var s in suggestions.Take(5))
                    Console.Error.WriteLine($"  [{s.QueryType}] {s.Query}");

                if (suggestions.Count > 0)
                    query = suggestions[0].Query;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                Console.Error.WriteLine("Provide --query \"text\" or --resume cv.docx");
                return;
            }

            Console.Error.WriteLine($"\nSearching: \"{query}\" (limit {limit} per adapter)...");
            var searchQuery = new JobSearchQuery(query, MaxResults: limit);
            var results = await searchService.SearchAllAsync(searchQuery, ct);

            Console.Error.WriteLine($"Found {results.Count} results\n");

            // Console output
            Console.Error.WriteLine($"{"#",-4} {"Title",-40} {"Company",-25} {"Location",-20} {"Source"}");
            Console.Error.WriteLine(new string('-', 100));
            for (var i = 0; i < Math.Min(results.Count, 20); i++)
            {
                var j = results[i];
                Console.Error.WriteLine($"{i + 1,-4} {Trunc(j.Title, 38),-40} {Trunc(j.Company, 23),-25} {Trunc(j.Location, 18),-20} {j.Source.Type}");
            }
            if (results.Count > 20)
                Console.Error.WriteLine($"  ... and {results.Count - 20} more");

            // JSON output
            if (output is not null)
            {
                var jsonOutput = results.Select(j => new
                {
                    title = j.Title,
                    company = j.Company,
                    location = j.Location,
                    isRemote = j.IsRemote,
                    salary = j.Salary is not null ? new { min = j.Salary.Min, max = j.Salary.Max, currency = j.Salary.Currency } : null,
                    url = j.Source.Url,
                    source = j.Source.Type.ToString(),
                    requiredSkills = j.RequiredSkills,
                });
                var json = JsonSerializer.Serialize(jsonOutput, JsonOpts);
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"\nWritten to {output.FullName}");
            }
        });

        return cmd;
    }

    private static string Trunc(string? s, int max) =>
        s is null ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";
}
