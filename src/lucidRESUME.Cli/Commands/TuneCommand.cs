using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Parsing.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume tune --folder ./samples/vacancy-cv-docx
///
/// Groups documents in the folder by their template fingerprint, then builds /
/// refines <see cref="TemplateParsingHints"/> for each group from all samples
/// that share the same template.  The more samples, the more complete the
/// section map and the more deterministic parsing becomes.
/// </summary>
public static class TuneCommand
{
    public static Command Build()
    {
        var folderOpt = new Option<DirectoryInfo?>("--folder") { Description = "Folder containing .docx files to analyse" };
        var patternOpt = new Option<string>("--pattern") { Description = "File glob pattern", DefaultValueFactory = _ => "*.docx" };
        var recursiveOpt = new Option<bool>("--recursive") { Description = "Search sub-folders" };
        var listOpt = new Option<bool>("--list") { Description = "List registry templates with hint status" };

        var cmd = new Command("tune", "Build/refine template parsing hints from sample DOCX files")
        {
            folderOpt, patternOpt, recursiveOpt, listOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var services = ServiceBootstrap.Build();
            var registry = services.GetRequiredService<TemplateRegistry>();

            if (parseResult.GetValue(listOpt))
            {
                await ListTemplatesAsync(registry, ct);
                return;
            }

            var folder = parseResult.GetValue(folderOpt);
            if (folder is null)
            {
                Console.Error.WriteLine("--folder is required unless --list is specified");
                return;
            }

            var pattern = parseResult.GetValue(patternOpt)!;
            var recursive = parseResult.GetValue(recursiveOpt);

            if (!folder.Exists)
            {
                Console.Error.WriteLine($"Folder not found: {folder.FullName}");
                return;
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folder.FullName, pattern, searchOption);
            Console.WriteLine($"Found {files.Length} file(s) in {folder}");

            // Group files by fingerprint hash
            var groups = new Dictionary<string, (string templateId, List<string> paths)>();

            foreach (var file in files)
            {
                var fp = TemplateFingerprint.FromFile(file);
                if (fp is null) { Console.WriteLine($"  SKIP {Path.GetFileName(file)} (unreadable)"); continue; }

                // Find matching template in registry
                var match = await registry.FindMatchAsync(fp, ct);
                if (match is null)
                {
                    Console.WriteLine($"  UNKNOWN {Path.GetFileName(file)} - not in registry (run train first)");
                    continue;
                }

                if (!groups.TryGetValue(match.Id, out var group))
                    groups[match.Id] = group = (match.Id, []);
                group.paths.Add(file);
                Console.WriteLine($"  GROUP   {Path.GetFileName(file)} → '{match.Name}' ({match.Id})");
            }

            Console.WriteLine();

            // Tune each group
            foreach (var (id, (_, paths)) in groups)
            {
                Console.Write($"Tuning template {id} from {paths.Count} sample(s)... ");
                await registry.UpdateHintsAsync(id, paths, ct);
                Console.WriteLine("done.");
            }

            // Summary
            var all = await registry.GetAllAsync(ct);
            Console.WriteLine();
            foreach (var t in all)
            {
                var hints = t.Hints;
                if (hints is null)
                    Console.WriteLine($"  {t.Id}  '{t.Name}'  - no hints");
                else
                    Console.WriteLine($"  {t.Id}  '{t.Name}'  - {hints.SampleCount} samples, " +
                                      $"{hints.SectionMap.Count} section mappings, " +
                                      $"usable={hints.IsUsable}");
            }
        });

        return cmd;
    }

    private static async Task ListTemplatesAsync(TemplateRegistry registry, CancellationToken ct)
    {
        var templates = await registry.GetAllAsync(ct);
        Console.WriteLine($"Registry: {templates.Count} template(s)");
        Console.WriteLine();
        foreach (var t in templates)
        {
            Console.WriteLine($"  id:      {t.Id}");
            Console.WriteLine($"  name:    {t.Name}");
            Console.WriteLine($"  matches: {t.MatchCount}");
            Console.WriteLine($"  hash:    {t.Fingerprint.Hash}");

            if (t.Hints is null)
            {
                Console.WriteLine($"  hints:   none");
            }
            else
            {
                Console.WriteLine($"  hints:   {t.Hints.SampleCount} samples, tuned {t.Hints.TunedAt:yyyy-MM-dd}");
                Console.WriteLine($"    name styles:    {string.Join(", ", t.Hints.NameStyleIds)}");
                Console.WriteLine($"    section styles: {string.Join(", ", t.Hints.SectionStyleIds)}");
                Console.WriteLine($"    sub-section:    {string.Join(", ", t.Hints.SubSectionStyleIds)}");
                Console.WriteLine($"    section map ({t.Hints.SectionMap.Count}):");
                foreach (var (k, v) in t.Hints.SectionMap)
                    Console.WriteLine($"      '{k}' → {v}");
            }
            Console.WriteLine();
        }
    }
}