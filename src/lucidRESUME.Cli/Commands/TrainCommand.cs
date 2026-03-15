using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Parsing.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume train --folder ./samples [--pattern *.docx]
///
/// Walks a folder of resume files and learns their DOCX template fingerprints
/// into the local registry. Run this once against a corpus of sample files to
/// pre-seed the registry before end-users import their own documents.
///
/// PDF files are skipped (fingerprinting is DOCX-only for now).
/// </summary>
public static class TrainCommand
{
    public static Command Build()
    {
        var folderOpt = new Option<DirectoryInfo>("--folder") { Required = true, Description = "Folder containing sample resume files" };
        folderOpt.Aliases.Add("-d");

        var patternOpt = new Option<string>("--pattern") { Description = "File glob pattern (default: *.docx)", DefaultValueFactory = _ => "*.docx" };
        patternOpt.Aliases.Add("-p");

        var recursiveOpt = new Option<bool>("--recursive") { Description = "Search sub-folders recursively" };
        recursiveOpt.Aliases.Add("-r");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };

        var cmd = new Command("train", "Seed the template registry from a folder of sample resume files")
        {
            folderOpt, patternOpt, recursiveOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var folder = result.GetValue(folderOpt)!;
            var pattern = result.GetValue(patternOpt)!;
            var recursive = result.GetValue(recursiveOpt);
            var config = result.GetValue(configOpt);

            if (!folder.Exists)
            {
                Console.Error.WriteLine($"Folder not found: {folder.FullName}");
                return;
            }

            var services = ServiceBootstrap.Build(config?.FullName);
            var registry = services.GetRequiredService<TemplateRegistry>();

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folder.FullName, pattern, searchOption);

            Console.Error.WriteLine($"Found {files.Length} file(s) matching '{pattern}' in {folder.FullName}");

            var learned = 0;
            var skipped = 0;

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(filePath);

                var fingerprint = TemplateFingerprint.FromFile(filePath);
                if (fingerprint is null)
                {
                    Console.Error.WriteLine($"  SKIP  {name} (could not fingerprint)");
                    skipped++;
                    continue;
                }

                await registry.LearnAsync(fingerprint, name, ct);
                Console.Error.WriteLine($"  LEARN {name} (hash={fingerprint.Hash})");
                learned++;
            }

            var all = await registry.GetAllAsync(ct);
            Console.Error.WriteLine($"\nDone. Learned={learned}, Skipped={skipped}, Registry total={all.Count}");
        });

        return cmd;
    }
}
