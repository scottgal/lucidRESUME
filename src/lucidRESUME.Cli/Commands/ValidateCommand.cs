using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume validate --resume cv.docx [--output report.json]
/// Quick sanity check before export — quality score, findings, completeness.
/// </summary>
public static class ValidateCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file to validate" };
        resumeOpt.Aliases.Add("-r");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON report" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("validate", "Quick quality check — score, completeness, issues")
        {
            resumeOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var quality = sp.GetRequiredService<IResumeQualityAnalyser>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);

            Console.Error.WriteLine("Validating...");
            var report = await quality.AnalyseAsync(resume, ct);

            var validation = new
            {
                file = file.Name,
                overallScore = report.OverallScore,
                pass = report.OverallScore >= 60,
                completeness = new
                {
                    hasName = !string.IsNullOrEmpty(resume.Personal.FullName),
                    hasEmail = !string.IsNullOrEmpty(resume.Personal.Email),
                    hasPhone = !string.IsNullOrEmpty(resume.Personal.Phone),
                    hasSummary = !string.IsNullOrEmpty(resume.Personal.Summary),
                    experienceCount = resume.Experience.Count,
                    skillCount = resume.Skills.Count,
                    educationCount = resume.Education.Count,
                },
                categories = report.Categories.Select(c => new
                {
                    name = c.Name,
                    score = c.Score,
                    weight = c.Weight,
                    errors = c.Findings.Count(f => f.Severity == Core.Models.Quality.FindingSeverity.Error),
                    warnings = c.Findings.Count(f => f.Severity == Core.Models.Quality.FindingSeverity.Warning),
                }),
                topIssues = report.Categories
                    .SelectMany(c => c.Findings)
                    .Where(f => f.Severity <= Core.Models.Quality.FindingSeverity.Warning)
                    .OrderBy(f => f.Severity)
                    .Take(10)
                    .Select(f => new
                    {
                        severity = f.Severity.ToString(),
                        code = f.Code,
                        message = f.Message,
                        section = f.Section,
                    }),
            };

            var json = JsonSerializer.Serialize(validation, JsonOpts);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(json);
            }

            var icon = validation.pass ? "PASS" : "FAIL";
            Console.Error.WriteLine($"\n{icon} — Score: {report.OverallScore}/100");
        });

        return cmd;
    }
}
