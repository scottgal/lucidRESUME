using System.CommandLine;
using lucidRESUME.AI;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume tailor --resume cv.docx --job "JD text or URL" [--output tailored.md]
/// Parses resume, matches against JD, compresses, tailors via LLM, evaluates quality.
/// </summary>
public static class TailorCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file (PDF or DOCX)" };
        resumeOpt.Aliases.Add("-r");

        var jobOpt = new Option<string>("--job") { Required = true, Description = "Job description text or URL" };
        jobOpt.Aliases.Add("-j");

        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file (default: stdout)" };
        outputOpt.Aliases.Add("-o");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };
        var evalOnlyOpt = new Option<bool>("--eval-only") { Description = "Only evaluate quality, don't tailor" };

        var cmd = new Command("tailor", "Tailor a resume for a specific job description")
        {
            resumeOpt, jobOpt, outputOpt, configOpt, evalOnlyOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var resumeFile = result.GetValue(resumeOpt)!;
            var jobText = result.GetValue(jobOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);
            var evalOnly = result.GetValue(evalOnlyOpt);

            if (!resumeFile.Exists)
            {
                Console.Error.WriteLine($"File not found: {resumeFile.FullName}");
                return;
            }

            var services = ServiceBootstrap.Build(config?.FullName);
            var resumeParser = services.GetRequiredService<IResumeParser>();
            var jobParser = services.GetRequiredService<IJobSpecParser>();
            var qualityAnalyser = services.GetRequiredService<IResumeQualityAnalyser>();
            var compressor = services.GetRequiredService<SemanticCompressor>();

            // Parse resume
            Console.Error.WriteLine($"Parsing {resumeFile.Name}...");
            var resume = await resumeParser.ParseAsync(resumeFile.FullName, ct);
            Console.Error.WriteLine($"  {resume.Skills.Count} skills, {resume.Experience.Count} positions");

            // Parse JD
            Console.Error.WriteLine("Parsing job description...");
            var jd = jobText.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await jobParser.ParseFromUrlAsync(jobText, ct)
                : await jobParser.ParseFromTextAsync(jobText, ct);
            Console.Error.WriteLine($"  Title: {jd.Title ?? "(unknown)"}, {jd.RequiredSkills.Count} required skills");

            // Quality before
            Console.Error.WriteLine("Evaluating original quality...");
            var beforeQuality = await qualityAnalyser.AnalyseAsync(resume, jd, ct);
            Console.Error.WriteLine($"  Original quality: {beforeQuality.OverallScore}/100");
            PrintFindings(beforeQuality);

            // Compress
            Console.Error.WriteLine("Compressing...");
            var compressed = await compressor.CompressAsync(resume, jd, ct);
            Console.Error.WriteLine($"  Fit: {compressed.OverallFit:P0}, {compressed.IncludedRoleCount}/{compressed.OriginalRoleCount} roles, {compressed.MatchedSkillCount}/{compressed.OriginalSkillCount} skills");
            if (compressed.Gaps.Count > 0)
                Console.Error.WriteLine($"  Gaps: {string.Join(", ", compressed.Gaps)}");

            if (evalOnly)
            {
                Console.Error.WriteLine("Eval-only mode — skipping tailoring.");
                return;
            }

            // Tailor
            var tailoringService = services.GetService<IAiTailoringService>();
            if (tailoringService is null || !tailoringService.IsAvailable)
            {
                Console.Error.WriteLine("No AI provider available. Start Ollama or configure API keys.");
                Console.Error.WriteLine("Use --eval-only to skip tailoring.");
                return;
            }

            Console.Error.WriteLine($"Tailoring via AI...");
            var profile = new UserProfile(); // empty profile for CLI
            var tailored = await tailoringService.TailorAsync(resume, jd, profile, ct);

            // Quality after
            Console.Error.WriteLine("Evaluating tailored quality...");
            var afterQuality = await qualityAnalyser.AnalyseAsync(tailored, jd, ct);
            Console.Error.WriteLine($"  Tailored quality: {afterQuality.OverallScore}/100 (was {beforeQuality.OverallScore}/100)");
            PrintFindings(afterQuality);

            var delta = afterQuality.OverallScore - beforeQuality.OverallScore;
            Console.Error.WriteLine($"\n  Quality delta: {(delta >= 0 ? "+" : "")}{delta} points");

            // Output
            var markdown = tailored.RawMarkdown ?? tailored.PlainText ?? "";
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

    private static void PrintFindings(Core.Models.Quality.QualityReport report)
    {
        foreach (var cat in report.Categories)
        {
            var errors = cat.Findings.Count(f => f.Severity == Core.Models.Quality.FindingSeverity.Error);
            var warnings = cat.Findings.Count(f => f.Severity == Core.Models.Quality.FindingSeverity.Warning);
            if (errors + warnings > 0)
                Console.Error.WriteLine($"    {cat.Name}: {cat.Score}/100 w{cat.Weight} ({errors} errors, {warnings} warnings)");
        }
    }
}
