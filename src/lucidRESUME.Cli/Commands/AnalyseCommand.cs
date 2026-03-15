using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume analyse --resume resume.pdf --job "url or text" [--output result.json]
/// Parses both a resume and job spec, then outputs a comparison summary.
/// </summary>
public static class AnalyseCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file to analyse (PDF or DOCX)" };
        resumeOpt.Aliases.Add("-r");

        var jobOpt = new Option<string>("--job") { Required = true, Description = "Job description: URL or pasted text" };
        jobOpt.Aliases.Add("-j");

        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file (default: stdout)" };
        outputOpt.Aliases.Add("-o");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };

        var cmd = new Command("analyse", "Compare a resume against a job description");
        cmd.Options.Add(resumeOpt);
        cmd.Options.Add(jobOpt);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var resume = result.GetValue(resumeOpt)!;
            var job = result.GetValue(jobOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            if (!resume.Exists)
            {
                Console.Error.WriteLine($"File not found: {resume.FullName}");
                return;
            }

            var services = ServiceBootstrap.Build(config?.FullName);
            var resumeParser = services.GetRequiredService<IResumeParser>();
            var jobParser = services.GetRequiredService<IJobSpecParser>();

            Console.Error.WriteLine($"Parsing resume: {resume.Name}");
            var resumeDoc = await resumeParser.ParseAsync(resume.FullName, ct);

            Console.Error.WriteLine("Parsing job spec...");
            var jobDoc = job.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await jobParser.ParseFromUrlAsync(job, ct)
                : await jobParser.ParseFromTextAsync(job, ct);

            var analysisResult = new
            {
                resume = resumeDoc,
                job = jobDoc,
                summary = new
                {
                    resumeName = resumeDoc.Personal.FullName,
                    jobTitle = jobDoc.Title,
                    company = jobDoc.Company,
                    isRemote = jobDoc.IsRemote,
                    isHybrid = jobDoc.IsHybrid,
                    salary = jobDoc.Salary is not null
                        ? $"{jobDoc.Salary.Min:N0}–{jobDoc.Salary.Max:N0}"
                        : null,
                    requiredSkillCount = jobDoc.RequiredSkills.Count,
                    preferredSkillCount = jobDoc.PreferredSkills.Count
                }
            };

            var json = JsonSerializer.Serialize(analysisResult, PrettyJson);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.WriteLine(json);
            }
        });

        return cmd;
    }
}
