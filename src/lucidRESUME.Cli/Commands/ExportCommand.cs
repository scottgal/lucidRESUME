using System.CommandLine;
using System.Text;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume export --file resume.pdf --format json|markdown [--output out.json]
/// Parses a resume then exports it in the chosen format.
/// </summary>
public static class ExportCommand
{
    public static Command Build()
    {
        var fileOpt = new Option<FileInfo>("--file", "-f")
        {
            Description = "Resume file (PDF or DOCX)",
            Required = true
        };

        var formatOpt = new Option<string>("--format", "-m")
        {
            Description = "Export format: json (JSON Resume) or markdown",
            Required = true
        };

        var outputOpt = new Option<FileInfo?>("--output", "-o")
        {
            Description = "Output file (default: stdout)"
        };

        var configOpt = new Option<FileInfo?>("--config")
        {
            Description = "Path to lucidresume.json config"
        };

        var cmd = new Command("export", "Parse a resume and export it in a structured format")
        {
            fileOpt, formatOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(fileOpt)!;
            var format = result.GetValue(formatOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            var services = ServiceBootstrap.Build(config?.FullName);
            var resumeParser = services.GetRequiredService<IResumeParser>();
            var exporters = services.GetServices<IResumeExporter>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await resumeParser.ParseAsync(file.FullName, ct);

            var fmt = format.ToLowerInvariant() switch
            {
                "json" or "jsonresume" => ExportFormat.JsonResume,
                "md" or "markdown"    => ExportFormat.Markdown,
                "docx" or "word"      => ExportFormat.Docx,
                "pdf"                 => ExportFormat.Pdf,
                _ => (ExportFormat?)null
            };

            if (fmt is null)
            {
                Console.Error.WriteLine($"Unknown format '{format}'. Use: json, markdown, docx, pdf");
                return;
            }

            var exporter = exporters.FirstOrDefault(e => e.Format == fmt.Value);
            if (exporter is null)
            {
                Console.Error.WriteLine($"No exporter registered for format {fmt}");
                return;
            }

            Console.Error.WriteLine($"Exporting as {fmt}...");
            var bytes = await exporter.ExportAsync(resume, ct);

            if (output is not null)
            {
                await File.WriteAllBytesAsync(output.FullName, bytes, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(Encoding.UTF8.GetString(bytes));
            }
        });

        return cmd;
    }
}
