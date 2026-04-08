using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume evidence --resume cv.docx [--output evidence.json]
/// Surfaces the full skill ledger with provenance chain.
/// </summary>
public static class EvidenceCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file" };
        resumeOpt.Aliases.Add("-r");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("evidence", "Surface the skill ledger with full evidence provenance")
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
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);

            Console.Error.WriteLine("Building skill ledger...");
            var ledger = await ledgerBuilder.BuildAsync(resume, ct);

            var evidenceOutput = new
            {
                basics = new
                {
                    name = resume.Personal.FullName,
                    email = resume.Personal.Email,
                    location = resume.Personal.Location,
                    github = resume.Personal.GitHubUrl,
                    linkedin = resume.Personal.LinkedInUrl,
                },
                skills = ledger.Entries.Select(e => new
                {
                    name = e.SkillName,
                    category = e.Category,
                    strength = Math.Round(e.Strength, 3),
                    calculatedYears = Math.Round(e.CalculatedYears, 1),
                    isCurrent = e.IsCurrent,
                    evidenceCount = e.Evidence.Count,
                    evidence = e.Evidence.Select(ev => new
                    {
                        source = ev.Source.ToString(),
                        company = ev.Company,
                        jobTitle = ev.JobTitle,
                        sourceText = ev.SourceText,
                        confidence = Math.Round(ev.Confidence, 2),
                        startDate = ev.StartDate?.ToString("yyyy-MM"),
                        endDate = ev.EndDate?.ToString("yyyy-MM"),
                    }),
                }),
                signals = new
                {
                    totalSkills = ledger.Entries.Count,
                    strongSkills = ledger.StrongSkills.Count,
                    weakSkills = ledger.WeakSkills.Count,
                    unsubstantiated = ledger.UnsubstantiatedSkills.Count,
                    consistencyIssues = ledger.Issues.Count,
                },
                experience = resume.Experience.Select(e => new
                {
                    company = e.Company,
                    title = e.Title,
                    startDate = e.StartDate?.ToString("yyyy-MM"),
                    endDate = e.IsCurrent ? "present" : e.EndDate?.ToString("yyyy-MM"),
                    technologies = e.Technologies,
                    importSources = e.ImportSources,
                }),
                education = resume.Education.Select(e => new
                {
                    institution = e.Institution,
                    degree = e.Degree,
                    fieldOfStudy = e.FieldOfStudy,
                    level = e.Level.ToString(),
                }),
            };

            var json = JsonSerializer.Serialize(evidenceOutput, JsonOpts);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(json);
            }

            Console.Error.WriteLine($"\n{ledger.Entries.Count} skills, {ledger.StrongSkills.Count} strong, {ledger.Issues.Count} issues");
        });

        return cmd;
    }
}
