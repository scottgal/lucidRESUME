using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume parse --file resume.pdf [--output result.json]
/// Parses a resume file and outputs the extracted schema as JSON.
/// </summary>
public static class ParseCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static Command Build()
    {
        var fileOpt = new Option<FileInfo>("--file") { Required = true, Description = "Resume file to parse (PDF or DOCX)" };
        fileOpt.Aliases.Add("-f");

        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file (default: stdout)" };
        outputOpt.Aliases.Add("-o");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };

        var cmd = new Command("parse", "Parse a resume file and extract structured data");
        cmd.Options.Add(fileOpt);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(fileOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            var services = ServiceBootstrap.Build(config?.FullName);
            var parser = services.GetRequiredService<IResumeParser>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await parser.ParseAsync(file.FullName, ct);
            if (resume.LlmEnhancementTask != null)
                await resume.LlmEnhancementTask;

            var json = JsonSerializer.Serialize(resume, PrettyJson);

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
