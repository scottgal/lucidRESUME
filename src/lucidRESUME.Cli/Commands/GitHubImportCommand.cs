using System.CommandLine;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

public static class GitHubImportCommand
{
    public static Command Build()
    {
        var usernameOpt = new Option<string>("--username") { Required = true, Description = "GitHub username to import repos from" };
        usernameOpt.Aliases.Add("-u");

        var configOpt = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };

        var cmd = new Command("github-import", "Import skills from GitHub public repos");
        cmd.Options.Add(usernameOpt);
        cmd.Options.Add(configOpt);

        cmd.SetAction(async (result, ct) =>
        {
            var username = result.GetValue(usernameOpt)!;
            var configPath = result.GetValue(configOpt)?.FullName;
            var sp = ServiceBootstrap.Build(configPath);
            var importer = sp.GetRequiredService<GitHubSkillImporter>();

            Console.WriteLine($"Importing skills from github.com/{username}...\n");
            var ghResult = await importer.ImportAsync(username, ct);

            if (ghResult.Profile is { } profile)
            {
                Console.WriteLine($"Profile: {profile.Name ?? profile.Login}");
                if (!string.IsNullOrEmpty(profile.Bio)) Console.WriteLine($"  Bio: {profile.Bio}");
                if (!string.IsNullOrEmpty(profile.Company)) Console.WriteLine($"  Company: {profile.Company}");
                if (!string.IsNullOrEmpty(profile.Location)) Console.WriteLine($"  Location: {profile.Location}");
                Console.WriteLine();
            }

            Console.WriteLine($"Repos: {ghResult.ReposAnalysed} analysed, {ghResult.ReposSkipped} skipped\n");

            Console.WriteLine($"Skills found: {ghResult.SkillEntries.Count}");
            Console.WriteLine($"{"Skill",-25} {"Category",-18} {"Strength",8} {"Years",6} {"Repos",5}");
            Console.WriteLine(new string('-', 70));
            foreach (var entry in ghResult.SkillEntries)
            {
                Console.WriteLine($"{entry.SkillName,-25} {entry.Category ?? "?",-18} {entry.Strength,8:F2} {entry.CalculatedYears,6:F1} {entry.Evidence.Count,5}");
            }

            if (ghResult.Warnings.Count > 0)
            {
                Console.WriteLine($"\nWarnings:");
                foreach (var w in ghResult.Warnings)
                    Console.WriteLine($"  - {w}");
            }

            Console.WriteLine($"\n{"=",-70}");
            Console.WriteLine($"Project Profiles: {ghResult.ProjectProfiles.Count}");
            Console.WriteLine($"{"=",-70}");
            foreach (var p in ghResult.ProjectProfiles.Take(15))
            {
                Console.WriteLine($"\n  {p.Name} ({p.PrimaryLanguage ?? "?"}) — {p.Stars} stars, {p.SizeKb}KB");
                Console.WriteLine($"  {p.Url}");
                Console.WriteLine($"  Active: {p.Created:yyyy-MM} → {p.LastActive:yyyy-MM} ({p.ActiveYears:F1}yr){(p.IsRecent ? " [recent]" : "")}");
                Console.WriteLine($"  Strength: {p.EvidenceStrength:F2}");
                if (p.Languages.Count > 0)
                    Console.WriteLine($"  Languages: {string.Join(", ", p.Languages.Select(l => $"{l.Language} {l.Fraction:P0}"))}");
                if (p.Topics.Count > 0)
                    Console.WriteLine($"  Topics: {string.Join(", ", p.Topics)}");
                if (p.Skills.Count > 0)
                    Console.WriteLine($"  Skills: {string.Join(", ", p.Skills.Take(10))}{(p.Skills.Count > 10 ? $" +{p.Skills.Count - 10} more" : "")}");
                if (p.ReadmeSkills.Count > 0)
                    Console.WriteLine($"  README skills: {string.Join(", ", p.ReadmeSkills.Take(8))}");
                if (!string.IsNullOrEmpty(p.Summary))
                {
                    var summary = p.Summary.Length > 150 ? p.Summary[..150] + "..." : p.Summary;
                    Console.WriteLine($"  Summary: {summary}");
                }
            }
            if (ghResult.ProjectProfiles.Count > 15)
                Console.WriteLine($"\n  ... and {ghResult.ProjectProfiles.Count - 15} more projects");
        });

        return cmd;
    }
}
