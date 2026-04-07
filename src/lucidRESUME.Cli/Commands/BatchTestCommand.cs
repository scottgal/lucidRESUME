using System.CommandLine;
using System.Diagnostics;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume batch-test --dir /path/to/resumes [--limit 50]
/// Fast batch testing: parse all resumes in a directory and report extraction quality stats.
/// </summary>
public static class BatchTestCommand
{
    public static Command Build()
    {
        var dirOpt = new Option<DirectoryInfo>("--dir") { Required = true, Description = "Directory containing resume files (PDF/DOCX)" };
        dirOpt.Aliases.Add("-d");
        var limitOpt = new Option<int>("--limit") { Description = "Max files to process (default 100)", DefaultValueFactory = _ => 100 };
        var configOpt = new Option<string?>("--config") { Description = "Path to config file" };

        var cmd = new Command("batch-test", "Parse multiple resumes and report extraction quality stats");
        cmd.Options.Add(dirOpt);
        cmd.Options.Add(limitOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var dir = result.GetValue(dirOpt)!;
            var limit = result.GetValue(limitOpt);
            var config = result.GetValue(configOpt);

            if (!dir.Exists)
            {
                Console.Error.WriteLine($"Directory not found: {dir.FullName}");
                return;
            }

            var sp = ServiceBootstrap.Build(config);
            var parser = sp.GetRequiredService<IResumeParser>();

            var files = dir.GetFiles("*.pdf").Concat(dir.GetFiles("*.docx"))
                .OrderBy(f => f.Name)
                .Take(limit)
                .ToList();

            Console.WriteLine($"Batch testing {files.Count} files from {dir.Name}");
            Console.WriteLine($"{"FILE",-40} {"NAME",-25} {"EXP",4} {"EDU",4} {"SKL",5} {"ENT",5} {"DOM",-20} {"MS",6}");
            Console.WriteLine(new string('-', 115));

            int totalExp = 0, totalSkl = 0, totalEnt = 0, zeroExp = 0, ok = 0, fail = 0;
            var sw = Stopwatch.StartNew();

            foreach (var file in files)
            {
                var fileSw = Stopwatch.StartNew();
                try
                {
                    var resume = await parser.ParseAsync(file.FullName, ct);
                    if (resume.LlmEnhancementTask != null)
                        await resume.LlmEnhancementTask;
                    SkillCategoriser.Categorise(resume);

                    var name = (resume.Personal.FullName ?? "?");
                    if (name.Length > 24) name = name[..24];
                    var exp = resume.Experience.Count;
                    var edu = resume.Education.Count;
                    var skl = resume.Skills.Count;
                    var ent = resume.Entities.Count;
                    var domain = DomainDetector.DetectPrimary(resume);
                    var ms = fileSw.ElapsedMilliseconds;

                    var fileName = file.Name.Length > 39 ? file.Name[..39] : file.Name;
                    Console.WriteLine($"{fileName,-40} {name,-25} {exp,4} {edu,4} {skl,5} {ent,5} {domain,-20} {ms,5}ms");

                    totalExp += exp; totalSkl += skl; totalEnt += ent;
                    if (exp == 0) zeroExp++;
                    ok++;
                }
                catch (Exception ex)
                {
                    var fileName = file.Name.Length > 39 ? file.Name[..39] : file.Name;
                    Console.WriteLine($"{fileName,-40} ERROR: {ex.Message[..Math.Min(70, ex.Message.Length)]}");
                    fail++;
                }
            }

            sw.Stop();
            Console.WriteLine(new string('-', 115));
            Console.WriteLine($"TOTAL: {ok} ok, {fail} errors, {sw.Elapsed.TotalSeconds:F1}s ({sw.Elapsed.TotalMilliseconds / Math.Max(ok, 1):F0}ms/resume)");
            Console.WriteLine($"  Avg exp={totalExp / (double)Math.Max(ok, 1):F1}  Avg skl={totalSkl / (double)Math.Max(ok, 1):F1}  Zero exp={zeroExp}/{ok} ({100.0 * zeroExp / Math.Max(ok, 1):F0}%)");
        });

        return cmd;
    }
}
