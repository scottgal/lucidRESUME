using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume explain --resume cv.docx --job "JD text" [--job-file jd.txt]
/// Shows WHY a match/tailor result happened — evidence chain, gaps, recommendations.
/// </summary>
public static class ExplainCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file" };
        resumeOpt.Aliases.Add("-r");
        var jobOpt = new Option<string?>("--job") { Description = "Job description text" };
        jobOpt.Aliases.Add("-j");
        var jobFileOpt = new Option<FileInfo?>("--job-file") { Description = "Job description file" };
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("explain", "Explain why a match score is what it is — full evidence chain")
        {
            resumeOpt, jobOpt, jobFileOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt)!;
            var jobText = result.GetValue(jobOpt);
            var jobFile = result.GetValue(jobFileOpt);
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            // Resolve JD text from --job or --job-file
            if (string.IsNullOrWhiteSpace(jobText) && jobFile is { Exists: true })
                jobText = await File.ReadAllTextAsync(jobFile.FullName, ct);
            if (string.IsNullOrWhiteSpace(jobText))
            {
                Console.Error.WriteLine("Provide --job \"text\" or --job-file jd.txt");
                return;
            }

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var jobParser = sp.GetRequiredService<IJobSpecParser>();
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();
            var jdLedgerBuilder = sp.GetRequiredService<JdSkillLedgerBuilder>();
            var matcher = sp.GetRequiredService<SkillLedgerMatcher>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);

            Console.Error.WriteLine("Parsing JD...");
            var jd = await jobParser.ParseFromTextAsync(jobText, ct);

            Console.Error.WriteLine("Building ledgers...");
            var resumeLedger = await ledgerBuilder.BuildAsync(resume, ct);
            var jdLedger = await jdLedgerBuilder.BuildAsync(jd, ct);

            Console.Error.WriteLine("Matching...");
            var matchResult = await matcher.MatchAsync(resumeLedger, jdLedger, resumeDoc: resume, ct: ct);

            var explanation = new
            {
                overallScore = Math.Round(matchResult.OverallFit, 3),
                coverage = new
                {
                    required = Math.Round(matchResult.RequiredCoverage, 3),
                    preferred = Math.Round(matchResult.PreferredCoverage, 3),
                },
                matchedSkills = matchResult.Matches
                    .Where(m => m.IsMatched)
                    .Select(m => new
                    {
                        skill = m.RequiredSkill,
                        importance = m.Importance.ToString(),
                        matchedTo = m.MatchedResumeSkill,
                        similarity = Math.Round(m.Similarity, 2),
                        evidenceStrength = Math.Round(m.EvidenceStrength, 2),
                        calculatedYears = Math.Round(m.CalculatedYears, 1),
                        evidenceCount = m.EvidenceCount,
                    }),
                missingSkills = matchResult.Matches
                    .Where(m => !m.IsMatched)
                    .Select(m => new
                    {
                        skill = m.RequiredSkill,
                        importance = m.Importance.ToString(),
                        nearestMatch = matchResult.NearMisses
                            .FirstOrDefault(nm => nm.RequiredSkill == m.RequiredSkill)?.ClosestResumeSkill,
                        nearestSimilarity = matchResult.NearMisses
                            .FirstOrDefault(nm => nm.RequiredSkill == m.RequiredSkill)?.Similarity,
                    }),
                risks = matchResult.Matches
                    .Where(m => m.IsMatched && m.EvidenceStrength < 0.3)
                    .Select(m => $"'{m.RequiredSkill}' matched but evidence is thin ({m.EvidenceCount} mentions)"),
                recommendations = matchResult.Matches
                    .Where(m => !m.IsMatched && m.Importance == Core.Models.Skills.SkillImportance.Required)
                    .Select(m =>
                    {
                        var nearMiss = matchResult.NearMisses.FirstOrDefault(nm => nm.RequiredSkill == m.RequiredSkill);
                        return nearMiss != null
                            ? $"Strengthen evidence for '{m.RequiredSkill}' — you have '{nearMiss.ClosestResumeSkill}' (similarity {nearMiss.Similarity:P0})"
                            : $"Missing '{m.RequiredSkill}' — no related skill found in resume";
                    }),
            };

            var json = JsonSerializer.Serialize(explanation, JsonOpts);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(json);
            }

            Console.Error.WriteLine($"\nScore: {matchResult.OverallFit:P0} | Required: {matchResult.RequiredCoverage:P0} | " +
                $"Matched: {matchResult.Matches.Count(m => m.IsMatched)}/{matchResult.Matches.Count}");
        });

        return cmd;
    }
}
