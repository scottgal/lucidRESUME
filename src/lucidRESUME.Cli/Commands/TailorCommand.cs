using System.CommandLine;
using lucidRESUME.AI;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume tailor --resume cv.docx --job "JD text or URL" [--output tailored.md]
/// Parses resume, matches against a JD, and renders a role-specific ledger projection.
/// </summary>
public static class TailorCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo?>("--resume") { Description = "Resume file (PDF or DOCX)" };
        resumeOpt.Aliases.Add("-r");
        var resumeDirOpt = new Option<DirectoryInfo?>("--resume-dir") { Description = "Directory of resume sources to merge into an evidence ledger" };

        var jobOpt = new Option<string?>("--job") { Description = "Job description text or URL" };
        jobOpt.Aliases.Add("-j");

        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file (default: stdout)" };
        outputOpt.Aliases.Add("-o");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };
        var evalOnlyOpt = new Option<bool>("--eval-only") { Description = "Only evaluate quality; do not project an output" };
        var formatOpt = new Option<string?>("--format") { Description = "Output format: markdown (default), docx, pdf, all" };
        var templateOpt = new Option<string?>("--template") { Description = "Output template: ats-classic, modern-professional, compact-technical" };
        var compactJobMlOpt = new Option<bool>("--cjobml")
        {
            DefaultValueFactory = _ => true,
            Description = "Include compact cJobML citations and References in exported files (default: true)"
        };
        var fullJobMlOpt = new Option<string?>("--full-jobml")
        {
            Description = "Published HTTPS URL of the complete career transcript/JobML record; compact references deep-link to its evidence sections"
        };

        var jobFileOpt = new Option<FileInfo?>("--job-file") { Description = "Job description file (alternative to --job)" };

        var cmd = new Command("tailor", "Project a resume from the ledger for a specific job description")
        {
            resumeOpt, resumeDirOpt, jobOpt, jobFileOpt, outputOpt, configOpt, evalOnlyOpt, formatOpt, templateOpt,
            compactJobMlOpt, fullJobMlOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var resumeFile = result.GetValue(resumeOpt);
            var resumeDirectory = result.GetValue(resumeDirOpt);
            var jobText = result.GetValue(jobOpt);
            var jobFile = result.GetValue(jobFileOpt);
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);
            var evalOnly = result.GetValue(evalOnlyOpt);
            var format = result.GetValue(formatOpt) ?? "markdown";
            var template = ResumeTemplateCatalog.Get(result.GetValue(templateOpt));
            var includeCompactJobMl = result.GetValue(compactJobMlOpt);
            var fullJobMl = result.GetValue(fullJobMlOpt);

            // Resolve JD from --job or --job-file
            if (string.IsNullOrWhiteSpace(jobText) && jobFile is { Exists: true })
                jobText = await File.ReadAllTextAsync(jobFile.FullName, ct);
            if (string.IsNullOrWhiteSpace(jobText))
            {
                Console.Error.WriteLine("Provide --job \"text\" or --job-file jd.txt");
                return;
            }

            await using var services = ServiceBootstrap.Build(config?.FullName);
            var jobParser = services.GetRequiredService<IJobSpecParser>();
            var qualityAnalyser = services.GetRequiredService<IResumeQualityAnalyser>();
            var compressor = services.GetRequiredService<SemanticCompressor>();

            // Parse resume (awaits LLM skill recovery if triggered)
            var resume = await ResumeInputHelper.LoadAsync(services, resumeFile, resumeDirectory, ct);
            if (!string.IsNullOrWhiteSpace(fullJobMl))
            {
                if (!Uri.TryCreate(fullJobMl, UriKind.Absolute, out var fullJobMlUri) ||
                    fullJobMlUri.Scheme is not ("http" or "https"))
                    throw new ArgumentException("--full-jobml must be an absolute HTTP or HTTPS URL.");
                resume.CompleteJobMlUri = fullJobMlUri.ToString();
            }
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
            Console.Error.WriteLine($"  Semantic evidence coverage: {compressed.OverallFit:P0} (not eligibility or job fit), {compressed.IncludedRoleCount}/{compressed.OriginalRoleCount} roles, {compressed.MatchedSkillCount}/{compressed.OriginalSkillCount} requirement terms");
            if (compressed.Gaps.Count > 0)
                Console.Error.WriteLine($"  Gaps: {string.Join(", ", compressed.Gaps)}");

            if (evalOnly)
            {
                Console.Error.WriteLine("Eval-only mode: skipping projection.");
                return;
            }

            Console.Error.WriteLine("Rendering deterministic evidence projection...");
            var artifact = services.GetRequiredService<ResumeArtifactBuilder>()
                .Build(resume, compressed.Projection, jd, template.Id);
            artifact.IncludeCompactJobMl = includeCompactJobMl;

            // Quality after
            Console.Error.WriteLine("Evaluating projected quality...");
            var afterQuality = await qualityAnalyser.AnalyseAsync(artifact, jd, ct);
            Console.Error.WriteLine($"  Projected quality: {afterQuality.OverallScore}/100 (was {beforeQuality.OverallScore}/100)");
            PrintFindings(afterQuality);

            var delta = afterQuality.OverallScore - beforeQuality.OverallScore;
            Console.Error.WriteLine($"\n  Quality delta: {(delta >= 0 ? "+" : "")}{delta} points");

            // Output
            await ResumeOutputWriter.WriteAsync(services, artifact, format, output, ct);
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
