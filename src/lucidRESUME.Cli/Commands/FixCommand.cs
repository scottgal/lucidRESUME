using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume fix --resume cv.docx [--output fixed.md] [--dry-run]
/// Auto-fixes obvious quality issues: weak verbs, filler words, pronoun usage.
/// Does NOT fabricate — only rewrites what's already there.
/// Verb replacements loaded from Resources/verb-replacements.txt.
/// </summary>
public static class FixCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file to fix" };
        resumeOpt.Aliases.Add("-r");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "Show what would change without applying" };

        var cmd = new Command("fix", "Auto-fix obvious quality issues — weak verbs, filler, pronouns")
        {
            resumeOpt, outputOpt, configOpt, dryRunOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt)!;
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);
            var dryRun = result.GetValue(dryRunOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();
            var quality = sp.GetRequiredService<IResumeQualityAnalyser>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);

            Console.Error.WriteLine("Analysing...");
            var report = await quality.AnalyseAsync(resume, ct);
            Console.Error.WriteLine($"  Before: {report.OverallScore}/100");

            // Load verb replacements from resource file
            var replacements = LoadVerbReplacements();
            var fillerPattern = LoadFillerPattern();

            var fixes = new List<string>();

            // Fix achievements
            foreach (var exp in resume.Experience)
            {
                for (var i = 0; i < exp.Achievements.Count; i++)
                {
                    var original = exp.Achievements[i];
                    var fixed_ = FixBullet(original, replacements, fillerPattern);
                    if (fixed_ != original)
                    {
                        fixes.Add($"  {original}\n  → {fixed_}");
                        if (!dryRun) exp.Achievements[i] = fixed_;
                    }
                }
            }

            // Fix summary pronouns
            if (resume.Personal.Summary is not null)
            {
                var fixedSummary = RemovePronouns(resume.Personal.Summary);
                if (fixedSummary != resume.Personal.Summary)
                {
                    fixes.Add("  Summary: removed first-person pronouns");
                    if (!dryRun) resume.Personal.Summary = fixedSummary;
                }
            }

            if (dryRun)
            {
                Console.Error.WriteLine($"\nDry run — {fixes.Count} fixes would be applied:");
                foreach (var f in fixes) Console.Error.WriteLine(f);
                return;
            }

            // Re-evaluate
            var afterReport = await quality.AnalyseAsync(resume, ct);
            var delta = afterReport.OverallScore - report.OverallScore;
            Console.Error.WriteLine($"  After: {afterReport.OverallScore}/100 ({fixes.Count} fixes, {(delta >= 0 ? "+" : "")}{delta} points)");

            // Output
            var exporter = sp.GetServices<IResumeExporter>()
                .FirstOrDefault(e => e.Format == ExportFormat.Markdown);
            if (exporter is not null)
            {
                var bytes = await exporter.ExportAsync(resume, ct);
                if (output is not null)
                {
                    await File.WriteAllBytesAsync(output.FullName, bytes, ct);
                    Console.Error.WriteLine($"Written to {output.FullName}");
                }
                else
                {
                    Console.Write(Encoding.UTF8.GetString(bytes));
                }
            }
        });

        return cmd;
    }

    private static string FixBullet(string bullet, Dictionary<string, string> replacements, Regex? fillerPattern)
    {
        var result = bullet;

        // Remove leading pronouns
        result = Regex.Replace(result, @"^(I |We |My |Our )", "", RegexOptions.IgnoreCase);

        // Replace weak verbs (from resource file)
        foreach (var (weak, strong) in replacements)
        {
            if (result.StartsWith(weak, StringComparison.OrdinalIgnoreCase))
            {
                result = strong + result[weak.Length..];
                break;
            }
        }

        // Remove filler words (from resource file)
        if (fillerPattern is not null)
            result = fillerPattern.Replace(result, "");

        // Clean up double spaces
        result = Regex.Replace(result, @"\s{2,}", " ");

        // Capitalise first letter
        if (result.Length > 0 && char.IsLower(result[0]))
            result = char.ToUpper(result[0]) + result[1..];

        return result.Trim();
    }

    private static string RemovePronouns(string text)
    {
        var result = Regex.Replace(text, @"\bI am\b", "A", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bI have\b", "Has", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bI've\b", "Has", RegexOptions.IgnoreCase);
        return result.Trim();
    }

    /// <summary>Load verb replacements from Resources/verb-replacements.txt</summary>
    private static Dictionary<string, string> LoadVerbReplacements()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(FixCommand).Assembly.Location) ?? "" })
        {
            var path = Path.Combine(dir, "Resources", "verb-replacements.txt");
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) continue;
                map.TryAdd(trimmed[..colonIdx].Trim(), trimmed[(colonIdx + 1)..].Trim());
            }
            return map;
        }
        return map;
    }

    /// <summary>Build filler word regex from Resources/fillers.txt</summary>
    private static Regex? LoadFillerPattern()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(FixCommand).Assembly.Location) ?? "" })
        {
            var path = Path.Combine(dir, "Resources", "fillers.txt");
            if (!File.Exists(path)) continue;
            var words = File.ReadLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();
            if (words.Count == 0) return null;
            var pattern = @"\b(" + string.Join("|", words.Select(Regex.Escape)) + @")\b\s*";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        return null;
    }
}
