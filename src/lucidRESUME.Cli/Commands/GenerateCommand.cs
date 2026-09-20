using System.CommandLine;
using System.Text;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume generate --resume cv.docx --prompt "2 page resume focused on cloud technologies"
///   [--output projected.md] [--format markdown|docx|pdf]
/// Projects a new resume from the evidence ledger, guided by a role prompt.
/// Never performs evidence inference while rendering.
/// </summary>
public static class GenerateCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo?>("--resume") { Description = "Source resume (evidence)" };
        resumeOpt.Aliases.Add("-r");
        var resumeDirOpt = new Option<DirectoryInfo?>("--resume-dir") { Description = "Directory of resume sources to merge into an evidence ledger" };
        var promptOpt = new Option<string>("--prompt") { Required = true, Description = "Projection target (e.g. '2 page resume focused on cloud tech')" };
        promptOpt.Aliases.Add("-p");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file" };
        outputOpt.Aliases.Add("-o");
        var formatOpt = new Option<string?>("--format") { Description = "Output format: markdown (default), docx, pdf, all" };
        var templateOpt = new Option<string?>("--template") { Description = "Output template: ats-classic, modern-professional, compact-technical" };
        var compactJobMlOpt = new Option<bool>("--cjobml")
        {
            DefaultValueFactory = _ => true,
            Description = "Include compact cJobML citations and References in exported files (default: true)"
        };
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("generate", "Project a resume from the evidence ledger for a target role")
        {
            resumeOpt, resumeDirOpt, promptOpt, outputOpt, formatOpt, templateOpt, configOpt, compactJobMlOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt);
            var resumeDirectory = result.GetValue(resumeDirOpt);
            var prompt = result.GetValue(promptOpt)!;
            var output = result.GetValue(outputOpt);
            var format = result.GetValue(formatOpt) ?? "markdown";
            var template = ResumeTemplateCatalog.Get(result.GetValue(templateOpt));
            var config = result.GetValue(configOpt);
            var includeCompactJobMl = result.GetValue(compactJobMlOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var resume = await ResumeInputHelper.LoadAsync(sp, file, resumeDirectory, ct);
            Console.Error.WriteLine($"  {resume.Skills.Count} skills, {resume.Experience.Count} positions");

            // Parse the target prompt once. This extracts target requirements; it does not
            // infer anything from candidate evidence or participate in rendering.
            var syntheticJd = await sp.GetRequiredService<IJobSpecParser>().ParseFromTextAsync(prompt, ct);
            syntheticJd.Title ??= prompt;

            Console.Error.WriteLine($"Projecting ledger with prompt: \"{prompt}\"...");
            var projected = await sp.GetRequiredService<lucidRESUME.AI.SemanticCompressor>()
                .CompressAsync(resume, syntheticJd, ct);
            var artifact = sp.GetRequiredService<ResumeArtifactBuilder>()
                .Build(resume, projected.Projection, syntheticJd, template.Id);
            artifact.IncludeCompactJobMl = includeCompactJobMl;

            await ResumeOutputWriter.WriteAsync(sp, artifact, format, output, ct);
        });

        return cmd;
    }
}
