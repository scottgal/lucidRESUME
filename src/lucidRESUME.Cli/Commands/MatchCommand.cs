using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

public static class MatchCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file (PDF or DOCX)" };
        resumeOpt.Aliases.Add("-r");
        var jobOpt = new Option<string>("--job") { Required = true, Description = "Job description text" };
        jobOpt.Aliases.Add("-j");
        var configOpt = new Option<string?>("--config") { Description = "Path to config file" };

        var cmd = new Command("match", "Show detailed skill ledger match between resume and job description");
        cmd.Options.Add(resumeOpt);
        cmd.Options.Add(jobOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var resumeFile = result.GetValue(resumeOpt)!;
            var jobText = result.GetValue(jobOpt)!;
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config);
            var parser = sp.GetRequiredService<IResumeParser>();
            var jdParser = sp.GetRequiredService<IJobSpecParser>();
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();
            var jdLedgerBuilder = sp.GetRequiredService<JdSkillLedgerBuilder>();
            var matcher = sp.GetRequiredService<SkillLedgerMatcher>();

            Console.WriteLine($"Parsing resume: {resumeFile.Name}");
            var resume = await parser.ParseAsync(resumeFile.FullName, ct);

            Console.WriteLine("Parsing job description...");
            var jd = await jdParser.ParseFromTextAsync(jobText, ct);

            Console.WriteLine("Building skill ledgers...");
            var resumeLedger = await ledgerBuilder.BuildAsync(resume, ct);
            var jdLedger = await jdLedgerBuilder.BuildAsync(jd, ct);

            Console.WriteLine($"\nResume: {resumeLedger.Entries.Count} skills in ledger");
            Console.WriteLine($"JD: {jdLedger.Requirements.Count} skill requirements");
            Console.WriteLine($"  Required: {jdLedger.Required.Count}");
            Console.WriteLine($"  Preferred: {jdLedger.Preferred.Count}");

            Console.WriteLine("\nJD Skill Terms (after extraction):");
            foreach (var req in jdLedger.Requirements)
                Console.WriteLine($"  [{req.Importance}] {req.SkillName}");

            Console.WriteLine("\nMatching...");
            var matchResult = await matcher.MatchAsync(resumeLedger, jdLedger, ct, resumeDoc: resume);

            Console.WriteLine("\n=== MATCH RESULTS ===");
            Console.WriteLine($"Overall Fit: {matchResult.OverallFit:P0}");
            Console.WriteLine($"Required Coverage: {matchResult.RequiredCoverage:P0}");
            Console.WriteLine($"Preferred Coverage: {matchResult.PreferredCoverage:P0}");
            Console.WriteLine($"Avg Evidence Strength: {matchResult.AverageEvidenceStrength:F2}");

            Console.WriteLine("\n=== PER-SKILL MATCHES ===");
            foreach (var m in matchResult.Matches.OrderByDescending(m => m.IsMatched).ThenByDescending(m => m.Similarity))
            {
                var status = m.IsMatched ? "MATCH" : "GAP  ";
                var resumeSkill = m.MatchedResumeSkill ?? "-";
                Console.WriteLine($"  [{status}] {m.RequiredSkill,-35} <- {resumeSkill,-30} sim={m.Similarity:F2} str={m.EvidenceStrength:F2} yrs={m.CalculatedYears:F1}");
            }

            if (matchResult.Gaps.Count > 0)
            {
                Console.WriteLine($"\n=== GAPS ({matchResult.Gaps.Count}) ===");
                foreach (var gap in matchResult.Gaps)
                    Console.WriteLine($"  - {gap}");
            }

            if (matchResult.NearMisses.Count > 0)
            {
                Console.WriteLine("\n=== NEAR MISSES ===");
                foreach (var nm in matchResult.NearMisses)
                    Console.WriteLine($"  {nm.RequiredSkill} ~ {nm.ClosestResumeSkill} (sim={nm.Similarity:F2})");
            }
        });

        return cmd;
    }
}
