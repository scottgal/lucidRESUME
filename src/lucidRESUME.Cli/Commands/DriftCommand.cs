using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume drift --resume1 old.docx --resume2 new.docx
/// Compares skill ledgers across two resume versions to detect temporal drift.
/// </summary>
public static class DriftCommand
{
    public static Command Build()
    {
        var resume1Opt = new Option<FileInfo>("--resume1") { Required = true, Description = "Earlier resume version" };
        var resume2Opt = new Option<FileInfo>("--resume2") { Required = true, Description = "Later resume version" };
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("drift", "Compare skill drift between two resume versions")
        {
            resume1Opt, resume2Opt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file1 = result.GetValue(resume1Opt)!;
            var file2 = result.GetValue(resume2Opt)!;
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();

            Console.Error.WriteLine($"Parsing {file1.Name}...");
            var r1 = await ParseHelper.ParseAndAwaitAsync(parser, file1.FullName, ct);
            Console.Error.WriteLine($"Parsing {file2.Name}...");
            var r2 = await ParseHelper.ParseAndAwaitAsync(parser, file2.FullName, ct);

            var ledger1 = await ledgerBuilder.BuildAsync(r1, ct);
            var ledger2 = await ledgerBuilder.BuildAsync(r2, ct);

            var report = SkillDriftAnalyser.Compare(ledger1, ledger2);

            Console.WriteLine($"\nSkill Drift: {file1.Name} → {file2.Name}");
            Console.WriteLine($"Total changes: {report.TotalChanges}\n");

            if (report.Added.Count > 0)
            {
                Console.WriteLine($"Added ({report.Added.Count}):");
                foreach (var d in report.Added)
                    Console.WriteLine($"  + {d.SkillName,-25} strength={d.NewStrength:F2} years={d.NewYears:F1}");
            }

            if (report.Dropped.Count > 0)
            {
                Console.WriteLine($"\nDropped ({report.Dropped.Count}):");
                foreach (var d in report.Dropped)
                    Console.WriteLine($"  - {d.SkillName,-25} was strength={d.OldStrength:F2}");
            }

            if (report.Strengthened.Count > 0)
            {
                Console.WriteLine($"\nStrengthened ({report.Strengthened.Count}):");
                foreach (var d in report.Strengthened)
                    Console.WriteLine($"  ↑ {d.SkillName,-25} {d.OldStrength:F2}→{d.NewStrength:F2} ({d.StrengthDelta:+0.00})");
            }

            if (report.Weakened.Count > 0)
            {
                Console.WriteLine($"\nWeakened ({report.Weakened.Count}):");
                foreach (var d in report.Weakened)
                    Console.WriteLine($"  ↓ {d.SkillName,-25} {d.OldStrength:F2}→{d.NewStrength:F2} ({d.StrengthDelta:+0.00})");
            }
        });

        return cmd;
    }
}
