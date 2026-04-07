using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// Match multiple resume variants against a JD using a compound skill ledger.
/// Each resume adds to the ledger - skills from CTO + AI Consultant + Lead Dev
/// compound into a richer profile than any single resume alone.
/// </summary>
public static class CompoundMatchCommand
{
    public static Command Build()
    {
        var resumesOpt = new Option<string[]>("--resumes") { Required = true, Description = "Resume files (comma-separated or multiple flags)" };
        resumesOpt.Aliases.Add("-r");
        resumesOpt.AllowMultipleArgumentsPerToken = true;
        var jobOpt = new Option<string>("--job") { Required = true, Description = "Job description text" };
        jobOpt.Aliases.Add("-j");
        var configOpt = new Option<string?>("--config") { Description = "Path to config file" };

        var cmd = new Command("compound-match", "Match multiple resume variants against a JD using compound skill ledger");
        cmd.Options.Add(resumesOpt);
        cmd.Options.Add(jobOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var resumeFiles = result.GetValue(resumesOpt)!;
            var jobText = result.GetValue(jobOpt)!;
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config);
            var parser = sp.GetRequiredService<IResumeParser>();
            var jdParser = sp.GetRequiredService<IJobSpecParser>();
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();
            var jdLedgerBuilder = sp.GetRequiredService<JdSkillLedgerBuilder>();
            var matcher = sp.GetRequiredService<SkillLedgerMatcher>();

            // Build compound ledger from all resumes
            var compoundEntries = new Dictionary<string, SkillLedgerEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in resumeFiles)
            {
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"File not found: {file}");
                    continue;
                }

                Console.WriteLine($"\n--- Ingesting: {Path.GetFileName(file)} ---");
                var resume = await parser.ParseAsync(file, ct);
                var ledger = await ledgerBuilder.BuildAsync(resume, ct);

                Console.WriteLine($"  Skills: {ledger.Entries.Count} | Strong: {ledger.StrongSkills.Count}");

                // Merge into compound ledger
                var added = 0;
                var strengthened = 0;
                foreach (var entry in ledger.Entries)
                {
                    if (compoundEntries.TryGetValue(entry.SkillName, out var existing))
                    {
                        // Merge evidence - add new evidence items
                        foreach (var evidence in entry.Evidence)
                        {
                            if (!existing.Evidence.Any(e => e.SourceText == evidence.SourceText))
                            {
                                existing.Evidence.Add(evidence);
                                strengthened++;
                            }
                        }
                    }
                    else
                    {
                        compoundEntries[entry.SkillName] = entry;
                        added++;
                    }
                }
                Console.WriteLine($"  Added: {added} new skills, strengthened: {strengthened} existing");
            }

            // Build compound SkillLedger
            var compoundLedger = new SkillLedger
            {
                Entries = compoundEntries.Values
                    .OrderByDescending(e => e.Strength)
                    .ToList()
            };

            Console.WriteLine($"\n=== COMPOUND LEDGER ===");
            Console.WriteLine($"Total skills: {compoundLedger.Entries.Count}");
            Console.WriteLine($"Strong (>0.5): {compoundLedger.StrongSkills.Count}");
            Console.WriteLine($"Weak: {compoundLedger.WeakSkills.Count}");
            Console.WriteLine($"Unsubstantiated: {compoundLedger.UnsubstantiatedSkills.Count}");

            // Show top 15 skills by strength
            Console.WriteLine($"\nTop 15 skills by strength:");
            foreach (var entry in compoundLedger.Entries.Take(15))
            {
                Console.WriteLine($"  {entry.SkillName,-35} str={entry.Strength:F2} yrs={entry.CalculatedYears:F1} roles={entry.RoleCount} evidence={entry.Evidence.Count}");
            }

            // Match against JD
            Console.WriteLine($"\nParsing JD...");
            var jd = await jdParser.ParseFromTextAsync(jobText, ct);
            var jdLedger = await jdLedgerBuilder.BuildAsync(jd, ct);

            // Match individual resumes
            Console.WriteLine($"\n=== INDIVIDUAL RESUME SCORES ===");
            foreach (var file in resumeFiles)
            {
                if (!File.Exists(file)) continue;
                var resume = await parser.ParseAsync(file, ct);
                var singleLedger = await ledgerBuilder.BuildAsync(resume, ct);
                var singleResult = await matcher.MatchAsync(singleLedger, jdLedger, ct);
                Console.WriteLine($"  {Path.GetFileName(file),-50} Fit: {singleResult.OverallFit:P0} (req {singleResult.RequiredCoverage:P0}, pref {singleResult.PreferredCoverage:P0})");
            }

            // Match compound ledger
            var compoundResult = await matcher.MatchAsync(compoundLedger, jdLedger, ct);

            Console.WriteLine($"\n=== COMPOUND MATCH ===");
            Console.WriteLine($"Overall Fit: {compoundResult.OverallFit:P0}");
            Console.WriteLine($"Required Coverage: {compoundResult.RequiredCoverage:P0}");
            Console.WriteLine($"Preferred Coverage: {compoundResult.PreferredCoverage:P0}");
            Console.WriteLine($"Avg Evidence Strength: {compoundResult.AverageEvidenceStrength:F2}");

            Console.WriteLine($"\n=== PER-SKILL ===");
            foreach (var m in compoundResult.Matches.OrderByDescending(m => m.IsMatched).ThenByDescending(m => m.Similarity))
            {
                var status = m.IsMatched ? "MATCH" : "GAP  ";
                Console.WriteLine($"  [{status}] {m.RequiredSkill,-40} <- {(m.MatchedResumeSkill ?? "-"),-30} sim={m.Similarity:F2} str={m.EvidenceStrength:F2}");
            }

            if (compoundResult.Gaps.Count > 0)
            {
                Console.WriteLine($"\n=== REMAINING GAPS ({compoundResult.Gaps.Count}) ===");
                foreach (var gap in compoundResult.Gaps)
                    Console.WriteLine($"  - {gap}");
            }
        });

        return cmd;
    }
}