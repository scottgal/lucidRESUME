using System.Text.Json;
using lucidRESUME.AI;
using lucidRESUME.Core.Models.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class ExtractionComparison
{
    public static async Task RunAsync(string output, string model, string endpoint, HttpClient http)
    {
        var extractor = new OllamaExtractionService(http, Options.Create(new OllamaOptions
        { BaseUrl = endpoint, ExtractionModel = model }), NullLogger<OllamaExtractionService>.Instance);
        var rows = new List<object>();
        foreach (var (id, source, expected) in new[]
        {
            ("negated-and-planned", "Skills and experience: Built C# services using SQL Server. No experience with Kubernetes. Python training is planned; I have not used Python.", new[] { "C#", "SQL Server" }),
            ("no-related-inference", "Experience: Worked on the ASP.NET team, implementing platform features. Built .NET components. No other languages or databases are listed.", new[] { "ASP.NET", ".NET" }),
            ("advert-is-not-experience", "My experience: Built C# APIs using SQL Server. Target job requirements: Docker, Kubernetes and Claude Code. These are vacancy requirements, not my skills.", new[] { "C#", "SQL Server" })
        })
        {
            var response = await extractor.ExtractSkillsAsync(source);
            var rawTerms = (response ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(term => EvidenceTerms.Canonicalize(term.TrimStart('-', '*', ' '))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var terms = SourceBoundSkillRecovery.Recover(source, response).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expected.Where(term => !terms.Contains(term)).ToArray();
            var unexpected = terms.Except(expected, StringComparer.OrdinalIgnoreCase).ToArray();
            rows.Add(new { id, source, expected, response, recovered = terms,
                raw_missing = expected.Except(rawTerms, StringComparer.OrdinalIgnoreCase), raw_unexpected = rawTerms.Except(expected, StringComparer.OrdinalIgnoreCase), missing, unexpected });
            await File.WriteAllTextAsync(Path.Combine(output, "extraction-comparison.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{id}: {missing.Length} missing; {unexpected.Length} unexpected.");
        }
    }
}
