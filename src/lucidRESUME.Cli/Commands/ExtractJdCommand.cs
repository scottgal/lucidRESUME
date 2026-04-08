using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume extract-jd --job "text" [--job-file jd.txt] [--output jd.json]
/// Extracts structured data from a job description — same pipeline employers and candidates share.
/// </summary>
public static class ExtractJdCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var jobOpt = new Option<string?>("--job") { Description = "Job description text" };
        jobOpt.Aliases.Add("-j");
        var jobFileOpt = new Option<FileInfo?>("--job-file") { Description = "Job description file" };
        var urlOpt = new Option<string?>("--url") { Description = "Job posting URL to scrape" };
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("extract-jd", "Extract structured data from a job description")
        {
            jobOpt, jobFileOpt, urlOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var jobText = result.GetValue(jobOpt);
            var jobFile = result.GetValue(jobFileOpt);
            var url = result.GetValue(urlOpt);
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            if (string.IsNullOrWhiteSpace(jobText) && jobFile is { Exists: true })
                jobText = await File.ReadAllTextAsync(jobFile.FullName, ct);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var jobParser = sp.GetRequiredService<IJobSpecParser>();

            Core.Models.Jobs.JobDescription jd;
            if (!string.IsNullOrWhiteSpace(url))
            {
                Console.Error.WriteLine($"Scraping {url}...");
                jd = await jobParser.ParseFromUrlAsync(url, ct);
            }
            else if (!string.IsNullOrWhiteSpace(jobText))
            {
                Console.Error.WriteLine("Parsing JD text...");
                jd = await jobParser.ParseFromTextAsync(jobText, ct);
            }
            else
            {
                Console.Error.WriteLine("Provide --job \"text\", --job-file jd.txt, or --url https://...");
                return;
            }

            var extracted = new
            {
                title = jd.Title,
                company = jd.Company,
                location = jd.Location,
                isRemote = jd.IsRemote,
                isHybrid = jd.IsHybrid,
                salary = jd.Salary is not null ? new
                {
                    min = jd.Salary.Min,
                    max = jd.Salary.Max,
                    currency = jd.Salary.Currency,
                    period = jd.Salary.Period,
                } : null,
                requiredSkills = jd.RequiredSkills,
                preferredSkills = jd.PreferredSkills,
                requiredYearsExperience = jd.RequiredYearsExperience,
                requiredEducation = jd.RequiredEducation,
                responsibilities = jd.Responsibilities,
                benefits = jd.Benefits,
                companyType = jd.CompanyType.ToString(),
            };

            // Archetype matching if taxonomy available
            var taxonomy = sp.GetService<lucidRESUME.Core.Interfaces.ISkillTaxonomy>();
            if (taxonomy is not null && jd.RequiredSkills.Count > 0)
            {
                var archetypes = taxonomy.GetArchetypeMatches(jd.RequiredSkills);
                Console.Error.WriteLine("\nRole archetypes:");
                foreach (var (role, pct, matched, total) in archetypes.Take(5))
                    Console.Error.WriteLine($"  {pct:P0} {role} ({matched}/{total} skills)");
            }

            var json = JsonSerializer.Serialize(extracted, JsonOpts);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(json);
            }

            Console.Error.WriteLine($"\nTitle: {jd.Title ?? "(unknown)"} | Skills: {jd.RequiredSkills.Count} required, {jd.PreferredSkills.Count} preferred");
        });

        return cmd;
    }
}
