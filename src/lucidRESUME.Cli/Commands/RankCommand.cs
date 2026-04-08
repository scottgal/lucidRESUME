using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume rank --dir resumes/ --job "JD text" [--job-file jd.txt] [--output ranking.json]
/// Given a directory of resumes and a JD, outputs a ranked list of matches.
/// The employer's view: "who fits this role best?"
/// </summary>
public static class RankCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var dirOpt = new Option<DirectoryInfo>("--dir") { Required = true, Description = "Directory of resume files" };
        dirOpt.Aliases.Add("-d");
        var jobOpt = new Option<string?>("--job") { Description = "Job description text" };
        jobOpt.Aliases.Add("-j");
        var jobFileOpt = new Option<FileInfo?>("--job-file") { Description = "Job description file" };
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var limitOpt = new Option<int>("--limit") { Description = "Max candidates to return" };
        
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("rank", "Rank resumes against a job description — the employer's view")
        {
            dirOpt, jobOpt, jobFileOpt, outputOpt, limitOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var dir = result.GetValue(dirOpt)!;
            var jobText = result.GetValue(jobOpt);
            var jobFile = result.GetValue(jobFileOpt);
            var output = result.GetValue(outputOpt);
            var limit = result.GetValue(limitOpt) is > 0 ? result.GetValue(limitOpt) : 20;
            var config = result.GetValue(configOpt);

            if (string.IsNullOrWhiteSpace(jobText) && jobFile is { Exists: true })
                jobText = await File.ReadAllTextAsync(jobFile.FullName, ct);
            if (string.IsNullOrWhiteSpace(jobText))
            {
                Console.Error.WriteLine("Provide --job \"text\" or --job-file jd.txt");
                return;
            }

            if (!dir.Exists)
            {
                Console.Error.WriteLine($"Directory not found: {dir.FullName}");
                return;
            }

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var jobParser = sp.GetRequiredService<IJobSpecParser>();
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();
            var jdLedgerBuilder = sp.GetRequiredService<JdSkillLedgerBuilder>();
            var matcher = sp.GetRequiredService<SkillLedgerMatcher>();

            Console.Error.WriteLine("Parsing JD...");
            var jd = await jobParser.ParseFromTextAsync(jobText, ct);
            var jdLedger = await jdLedgerBuilder.BuildAsync(jd, ct);

            Console.Error.WriteLine($"JD: {jd.Title ?? "(unknown)"} | {jd.RequiredSkills.Count} required skills");

            var files = dir.GetFiles("*.docx").Concat(dir.GetFiles("*.pdf")).Concat(dir.GetFiles("*.txt")).ToList();
            Console.Error.WriteLine($"Found {files.Count} resumes in {dir.Name}/");

            var rankings = new List<(string File, string Name, double Score, double Required, int Matched, int Total)>();

            foreach (var file in files)
            {
                try
                {
                    var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);
                    var resumeLedger = await ledgerBuilder.BuildAsync(resume, ct);
                    var matchResult = await matcher.MatchAsync(resumeLedger, jdLedger, resumeDoc: resume, ct: ct);

                    var name = resume.Personal.FullName ?? Path.GetFileNameWithoutExtension(file.Name);
                    rankings.Add((file.Name, name, matchResult.OverallFit, matchResult.RequiredCoverage,
                        matchResult.Matches.Count(m => m.IsMatched), matchResult.Matches.Count));

                    Console.Error.Write(".");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\n  Skip {file.Name}: {ex.Message}");
                }
            }

            Console.Error.WriteLine();

            var ranked = rankings.OrderByDescending(r => r.Score).Take(limit).ToList();

            // Console output
            Console.Error.WriteLine($"\n{"Rank",-5} {"Score",-8} {"Required",-10} {"Skills",-10} {"Name",-30} {"File"}");
            Console.Error.WriteLine(new string('-', 90));
            for (var i = 0; i < ranked.Count; i++)
            {
                var r = ranked[i];
                Console.Error.WriteLine($"{i + 1,-5} {r.Score,-8:P0} {r.Required,-10:P0} {r.Matched}/{r.Total,-7} {r.Name,-30} {r.File}");
            }

            // JSON output
            var jsonOutput = ranked.Select((r, i) => new
            {
                rank = i + 1,
                file = r.File,
                name = r.Name,
                overallScore = Math.Round(r.Score, 3),
                requiredCoverage = Math.Round(r.Required, 3),
                matchedSkills = r.Matched,
                totalSkills = r.Total,
            });

            if (output is not null)
            {
                var json = JsonSerializer.Serialize(jsonOutput, JsonOpts);
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"\nWritten to {output.FullName}");
            }
        });

        return cmd;
    }
}
