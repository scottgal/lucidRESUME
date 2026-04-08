using System.CommandLine;
using System.Text;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
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
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Source resume (evidence)" };
        resumeOpt.Aliases.Add("-r");
        var promptOpt = new Option<string>("--prompt") { Required = true, Description = "Generation prompt (e.g. '2 page resume focused on cloud tech')" };
        promptOpt.Aliases.Add("-p");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file" };
        outputOpt.Aliases.Add("-o");
        var formatOpt = new Option<string?>("--format") { Description = "Output format: markdown (default), docx, pdf" };
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("generate", "Generate a resume from your evidence using a prompt — never fabricates")
        {
            resumeOpt, promptOpt, outputOpt, formatOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt)!;
            var prompt = result.GetValue(promptOpt)!;
            var output = result.GetValue(outputOpt);
            var format = result.GetValue(formatOpt) ?? "markdown";
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var tailoringService = sp.GetService<IAiTailoringService>();

            if (tailoringService is null)
            {
                Console.Error.WriteLine("No AI provider registered.");
                return;
            }

            // Check availability
            if (tailoringService is AI.OllamaTailoringService ollama)
                await ollama.CheckAvailabilityAsync(ct);
            if (!tailoringService.IsAvailable)
            {
                Console.Error.WriteLine("AI provider not reachable. Start Ollama or configure API keys.");
                return;
            }

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);
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

            // Export
            var markdown = generated.RawMarkdown ?? generated.PlainText ?? "";

            if (format.Equals("docx", StringComparison.OrdinalIgnoreCase) || format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            {
                var fmt = format.Equals("docx", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Docx : ExportFormat.Pdf;
                var exporter = sp.GetServices<IResumeExporter>().FirstOrDefault(e => e.Format == fmt);
                if (exporter is not null)
                {
                    // Parse the generated markdown back to a document for export
                    generated.PlainText = markdown;
                    var bytes = await exporter.ExportAsync(generated, ct);
                    if (output is not null)
                    {
                        await File.WriteAllBytesAsync(output.FullName, bytes, ct);
                        Console.Error.WriteLine($"Written {format.ToUpper()} to {output.FullName}");
                    }
                    else
                    {
                        await Console.OpenStandardOutput().WriteAsync(bytes, ct);
                    }
                    return;
                }
            }

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, markdown, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(markdown);
            }
        });

        return cmd;
    }
}
