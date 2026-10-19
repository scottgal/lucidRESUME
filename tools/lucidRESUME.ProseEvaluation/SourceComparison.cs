using System.Text.Json;
using lucidRESUME.AI;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.JobSpec;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Manually annotated source comparisons. Expected facts are written independently of parser output.</summary>
internal static class SourceComparison
{
    public static async Task<int> RunAsync(string output)
    {
        var results = new List<object>();
        var failures = 0;
        void Record(string id, string source, object parsed, Dictionary<string, bool> checks)
        {
            failures += checks.Count(check => !check.Value);
            results.Add(new { id, source, expected_checks = checks, parsed });
        }
        foreach (var (id, source, employer, expected) in new[]
        {
            ("short-evidence", "# Alex Example\n## Experience\n### Engineer | Example Platform\nJan 2020 - Dec 2022\n- Built C# APIs.\n- Used SQL Server.\n- Mentored peers.", "Example Platform", new[] { "Built C# APIs.", "Used SQL Server.", "Mentored peers." }),
            ("wrapped-evidence", "# Alex Example\n## Experience\n### Engineer | Example Platform\nJan 2020 - Dec 2022\n- Designed the recovery workflow for the billing service\n  using C# and SQL Server;\n  not yet deployed.", "Example Platform", new[] { "Designed the recovery workflow for the billing service using C# and SQL Server; not yet deployed." }),
            ("separate-engagements", "# Alex Example\n## Experience\n### Consultant | Example Platform\nJan 2020 - Dec 2021\n- Built ASP.NET Core integrations for billing.\n### Consultant | Example Platform\nJan 2023 - Dec 2024\n- Evaluated a Python prototype; it never entered production.", "Example Platform", new[] { "Built ASP.NET Core integrations for billing.", "Evaluated a Python prototype; it never entered production." }),
            ("hyphenated-identity", "# Alex Example\n## Experience\n### Senior .NET Developer — North-West Systems\nJan 2020 - Dec 2022\n- Built SQL Server reporting queries and recovery drills.", "North-West Systems", new[] { "Built SQL Server reporting queries and recovery drills." })
        })
        {
            var parsed = ResumeDocument.Create(id + ".md", "text/markdown", source.Length);
            MarkdownSectionParser.PopulateSections(parsed, source);
            Record(id, source, parsed.Experience, new()
            {
                ["correct employer"] = parsed.Experience.All(role => role.Company == employer) && parsed.Experience.Count > 0,
                ["exact source accounts preserved"] = expected.All(text => parsed.Experience.SelectMany(role => role.Achievements).Contains(text)),
                ["no invented source accounts"] = parsed.Experience.SelectMany(role => role.Achievements).All(expected.Contains),
                ["separate engagement dates retained"] = id != "separate-engagements" || parsed.Experience.Count == 2 && parsed.Experience[0].StartDate?.Year == 2020 && parsed.Experience[1].StartDate?.Year == 2023
            });
        }
        var parser = new JobSpecParser(NullLogger<JobSpecParser>.Instance);
        foreach (var (id, source) in new[]
        {
            ("inline-priority", "Development Team Lead at Example\nEssential Experience building C# services with SQL Server and Service Bus.\nNice to Have Docker and Kubernetes.\nBenefits\nAnnual training and private medical insurance."),
            ("header-chrome", "About the job\nThis is a remote first UK position.\nWe are looking for a Development Team Lead to lead four engineers.\nEssential\nC# and SQL Server.\nNice to Have\nDocker."),
            ("qualification-is-optional", "Engineer at Example\nWho You Are\nStrong C# and SQL Server experience.\nPreferably 10+ years of engineering experience.\nNice to Have\nDocker.\nBenefits\nExperience a supportive family culture and annual training."),
            ("negative-domain", "Engineer at Example\nWhat we need\nYou don't need to have worked with Kubernetes.\nEssential C# and SQL Server.\nNice to Have\nDocker.")
        })
        {
            var job = await parser.ParseFromTextAsync(source);
            var writing = ProseJobRequirements.Build(job);
            Record(id, source, new { job.Title, job.RequiredSkills, job.PreferredSkills, writing }, new()
            {
                ["correct role identity"] = job.Title == (id is "inline-priority" or "header-chrome" ? "Development Team Lead" : "Engineer"),
                ["C# and SQL Server required"] = job.RequiredSkills.Contains("C#") && job.RequiredSkills.Contains("SQL Server"),
                ["Docker optional"] = job.PreferredSkills.Contains("Docker") && !job.RequiredSkills.Contains("Docker"),
                ["benefits excluded from writing focus"] = !writing.Any(item => item.Text.Contains("annual training", StringComparison.OrdinalIgnoreCase) || item.Text.Contains("supportive family", StringComparison.OrdinalIgnoreCase)),
                ["optional tenure not mandatory"] = (id != "qualification-is-optional" || job.RequiredYearsExperience is null) && !writing.Any(item => item.Text.StartsWith("Preferably", StringComparison.OrdinalIgnoreCase) && item.Kind.ToString() != "Preferred"),
                ["negated domain not a writing demand"] = !writing.Any(item => item.Text.Contains("don't need", StringComparison.OrdinalIgnoreCase))
            });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "source-comparison.json"), JsonSerializer.Serialize(new
        {
            annotation = "Source expectations authored by the reviewing coding agent before tuning. Diagnostic regression cases; no claim of independent or held-out accuracy.",
            failures, cases = results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Source comparison: {results.Count} cases, {failures} failed checks.");
        return failures == 0 ? 0 : 1;
    }
}
