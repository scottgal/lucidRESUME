using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume parse --file resume.md [--output result.json]
/// Parses a resume file and outputs the extracted schema as JSON.
/// </summary>
public static class ParseCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static Command Build()
    {
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "Resume file to parse (Markdown, PDF, DOCX, DOC, or TXT)"
        };
        fileOpt.Aliases.Add("-f");
        var directoryOpt = new Option<DirectoryInfo?>("--resume-dir") { Description = "Directory of resume sources to merge into one evidence ledger" };

        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file (default: stdout)" };
        outputOpt.Aliases.Add("-o");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };

        var cmd = new Command("parse", "Parse a resume file and extract structured data");
        cmd.Options.Add(fileOpt);
        cmd.Options.Add(directoryOpt);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(fileOpt);
            var directory = result.GetValue(directoryOpt);
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            if (file is not null && file.Extension.ToLowerInvariant() is not ".pdf" and not ".docx" and not ".doc"
                and not ".txt" and not ".md" and not ".markdown")
            {
                Console.Error.WriteLine($"Unsupported file type '{file.Extension}'. Supported formats: .doc, .docx, .md, .markdown, .pdf, .txt");
                return;
            }

            using var services = ServiceBootstrap.Build(config?.FullName);
            var resume = await ResumeInputHelper.LoadAsync(services, file, directory, ct);

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
