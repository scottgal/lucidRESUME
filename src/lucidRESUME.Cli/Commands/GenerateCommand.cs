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
///   [--output generated.md] [--format markdown|docx|pdf]
/// Generates a new resume from the skill ledger using an LLM, guided by a prompt.
/// Never fabricates — only uses evidence already in the ledger.
/// </summary>
public static class GenerateCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo?>("--resume") { Description = "Source resume (evidence)" };
        resumeOpt.Aliases.Add("-r");
        var resumeDirOpt = new Option<DirectoryInfo?>("--resume-dir") { Description = "Directory of resume sources to merge into an evidence ledger" };
        var promptOpt = new Option<string>("--prompt") { Required = true, Description = "Generation prompt (e.g. '2 page resume focused on cloud tech')" };
        promptOpt.Aliases.Add("-p");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file" };
        outputOpt.Aliases.Add("-o");
        var formatOpt = new Option<string?>("--format") { Description = "Output format: markdown (default), docx, pdf, all" };
        var templateOpt = new Option<string?>("--template") { Description = "Output template: ats-classic, modern-professional, compact-technical" };
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("generate", "Generate a resume from your evidence using a prompt — never fabricates")
        {
            resumeOpt, resumeDirOpt, promptOpt, outputOpt, formatOpt, templateOpt, configOpt
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

            var sp = ServiceBootstrap.Build(config?.FullName);
            var tailoringService = sp.GetService<IAiTailoringService>();

            if (tailoringService is null)
            {
                Console.Error.WriteLine("No AI provider registered.");
                return;
            }

            await tailoringService.CheckAvailabilityAsync(ct);
            if (!tailoringService.IsAvailable)
            {
                Console.Error.WriteLine("AI provider unavailable. For LLamaSharp, download the configured GGUF model; otherwise check the selected provider's configuration.");
                return;
            }

            var resume = await ResumeInputHelper.LoadAsync(sp, file, resumeDirectory, ct);
            Console.Error.WriteLine($"  {resume.Skills.Count} skills, {resume.Experience.Count} positions");

            // Build a synthetic JD from the prompt to guide tailoring
            var syntheticJd = Core.Models.Jobs.JobDescription.Create(
                $"Generate a resume with these specifications: {prompt}",
                new Core.Models.Jobs.JobSource { Type = Core.Models.Jobs.JobSourceType.PastedText });
            syntheticJd.Title = prompt;

            Console.Error.WriteLine($"Generating with prompt: \"{prompt}\"...");
            var profile = new UserProfile
            {
                AdditionalContext = $"GENERATION INSTRUCTIONS: {prompt}. " +
                    "Use ONLY the evidence from the candidate's actual experience. " +
                    "Do NOT invent any skills, roles, or achievements. " +
                    "Focus and prioritise based on the prompt, but never fabricate."
            };

            var generated = await tailoringService.TailorAsync(resume, syntheticJd, profile, ct);
            var artifact = sp.GetRequiredService<ResumeArtifactBuilder>()
                .Build(resume, generated, syntheticJd, template.Id);

            await ResumeOutputWriter.WriteAsync(sp, artifact, format, output, ct);
        });

        return cmd;
    }
}
