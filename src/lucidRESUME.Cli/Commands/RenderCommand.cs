using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.JobML;

namespace lucidRESUME.Cli.Commands;

public static class RenderCommand
{
    public static Command Build()
    {
        var fileOption = new Option<FileInfo>("--file", "-f")
        {
            Required = true,
            Description = "Portable Markdown file containing an embedded JobML block"
        };
        var outputOption = new Option<FileInfo>("--output", "-o")
        {
            Required = true,
            Description = "Output path or basename"
        };
        var formatOption = new Option<string>("--format", "-m")
        {
            DefaultValueFactory = _ => "all",
            Description = "Output format: markdown, docx, pdf, or all"
        };
        var templateOption = new Option<string?>("--template")
        {
            Description = "Output template: ats-classic, modern-professional, compact-technical"
        };
        var configOption = new Option<FileInfo?>("--config") { Description = "Config file" };

        var command = new Command("render", "Render a portable JobML resume without calling an AI provider")
        {
            fileOption, outputOption, formatOption, templateOption, configOption
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var file = result.GetValue(fileOption)!;
            var output = result.GetValue(outputOption)!;
            var source = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            var parsed = new JobMlParser().Parse(source);
            var resume = ResumeDocument.Create(file.Name, "text/markdown", file.Length);
            resume.SetDoclingOutput(parsed.Markdown, null, parsed.Markdown);
            resume.CanonicalMarkdown = parsed.Markdown;
            resume.JobMlSource = source;
            resume.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(parsed.Markdown);
            resume.OutputTemplateId = ResumeTemplateCatalog.Get(result.GetValue(templateOption)).Id;
            MarkdownSectionParser.PopulateSections(resume, parsed.Markdown);

            var services = ServiceBootstrap.Build(result.GetValue(configOption)?.FullName);
            await ResumeOutputWriter.WriteAsync(
                services, resume, result.GetValue(formatOption)!, output, cancellationToken);
        });
        return command;
    }
}
