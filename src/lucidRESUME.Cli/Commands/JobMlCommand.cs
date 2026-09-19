using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Ingestion.Web;
using lucidRESUME.JobML;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

public static class JobMlCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Command Build()
    {
        var command = new Command("jobml", "Validate and inspect reversible JobML documents");
        command.Subcommands.Add(BuildValidate());
        command.Subcommands.Add(BuildReconcile());
        command.Subcommands.Add(BuildCoverage());
        command.Subcommands.Add(BuildColdParserProbe());
        command.Subcommands.Add(BuildCompact());
        command.Subcommands.Add(BuildLinkPost());
        return command;
    }

    private static Command BuildCompact()
    {
        var fileOption = FileOption();
        var outputOption = new Option<FileInfo?>("--output")
        { Description = "Write cJobML Markdown to this file instead of stdout" };
        outputOption.Aliases.Add("-o");
        var ledgerOption = new Option<string?>("--complete-ledger")
        { Description = "Public absolute URL for the complete full-resolution JobML ledger" };
        var command = new Command("compact", "Project full JobML as inline xrefs and a compact JATS-like reference list")
            { fileOption, outputOption, ledgerOption };
        command.SetAction(async (result, cancellationToken) =>
        {
            var parsed = await ParseAsync(result.GetValue(fileOption)!, cancellationToken);
            var ledger = result.GetValue(ledgerOption);
            if (!string.IsNullOrWhiteSpace(ledger))
            {
                if (!Uri.TryCreate(ledger, UriKind.Absolute, out _))
                    throw new ArgumentException("--complete-ledger must be an absolute URL.");
                parsed.Data.Document.CompleteLedger = ledger;
            }

            var errors = JobMlProcessor.Validate(parsed)
                .Where(diagnostic => diagnostic.Severity == JobMlDiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
                throw new InvalidDataException(
                    $"Cannot publish cJobML from invalid full JobML: {string.Join("; ", errors.Select(error => error.Message))}");

            var compact = CJobMlProjector.Project(parsed).Markdown;
            var output = result.GetValue(outputOption);
            if (output is null) Console.Write(compact);
            else
            {
                await File.WriteAllTextAsync(output.FullName, compact, cancellationToken);
                Console.Error.WriteLine($"Written cJobML to {output.FullName}");
            }
        });
        return command;
    }

    private static Command BuildLinkPost()
    {
        var fileOption = FileOption();
        var claimOption = new Option<string>("--claim")
        { Required = true, Description = "Stable JobML claim id supported by the article" };
        var urlOption = new Option<string>("--url")
        { Required = true, Description = "Canonical or public article URL" };
        var outputOption = new Option<FileInfo?>("--output")
        { Description = "Write updated full JobML to this file instead of stdout" };
        outputOption.Aliases.Add("-o");
        var command = new Command("link-post", "Import a post as reviewed external evidence for an existing claim")
            { fileOption, claimOption, urlOption, outputOption };
        command.SetAction(async (result, cancellationToken) =>
        {
            var parsed = await ParseAsync(result.GetValue(fileOption)!, cancellationToken);
            var claimId = result.GetValue(claimOption)!;
            var claim = parsed.Data.Claims.SingleOrDefault(item =>
                            string.Equals(item.Id, claimId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"Claim '{claimId}' was not found.");
            if (!Uri.TryCreate(result.GetValue(urlOption), UriKind.Absolute, out var url) ||
                url.Scheme is not ("http" or "https"))
                throw new ArgumentException("--url must be an absolute HTTP or HTTPS URL.");

            var services = ServiceBootstrap.Build();
            var linkedPost = await services.GetRequiredService<LinkedPostImporter>()
                .ImportAsync(url, cancellationToken);
            if (!claim.Evidence.Any(item => string.Equals(item.Uri?.TrimEnd('/'),
                    linkedPost.CanonicalUri.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                claim.Evidence.Add(linkedPost.ToJobMlEvidence());

            var serialized = new JobMlParser().Serialize(parsed);
            var output = result.GetValue(outputOption);
            if (output is null) Console.Write(serialized);
            else
            {
                await File.WriteAllTextAsync(output.FullName, serialized, cancellationToken);
                Console.Error.WriteLine($"Linked '{linkedPost.Title}' and wrote {output.FullName}");
            }
        });
        return command;
    }

    private static Command BuildColdParserProbe()
    {
        var fileOption = FileOption();
        var command = new Command(
            "cold-parser-probe",
            "Emit a provider-neutral cold-parser challenge and deterministic ground truth")
        {
            fileOption
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var file = result.GetValue(fileOption)!;
            var source = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            var parsed = new JobMlParser().Parse(source);
            var probe = JobMlColdParserProbe.Create(source, parsed);
            var options = new JsonSerializerOptions(JsonOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            Console.WriteLine(JsonSerializer.Serialize(probe, options));
        });
        return command;
    }

    private static Command BuildValidate()
    {
        var fileOption = FileOption();
        var command = new Command("validate", "Validate schema, references, evidence drift, and review state")
        {
            fileOption
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var parsed = await ParseAsync(result.GetValue(fileOption)!, cancellationToken);
            var diagnostics = JobMlProcessor.Validate(parsed);
            Console.WriteLine(JsonSerializer.Serialize(diagnostics, JsonOptions));
            Console.Error.WriteLine(diagnostics.Any(x => x.Severity == JobMlDiagnosticSeverity.Error)
                ? "INVALID"
                : "VALID");
        });
        return command;
    }

    private static Command BuildReconcile()
    {
        var fileOption = FileOption();
        var command = new Command("reconcile", "Report live evidence state without changing prose or claims")
        {
            fileOption
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var parsed = await ParseAsync(result.GetValue(fileOption)!, cancellationToken);
            var report = JobMlProcessor.Reconcile(parsed).Select(claim => new
            {
                claim = claim.Claim.Id,
                evidence = claim.Evidence.Select(item => new
                {
                    reference = item.Evidence.Ref ?? item.Evidence.Uri,
                    state = item.State.ToString().ToLowerInvariant(),
                    item.SuggestedReference,
                    item.CurrentText
                })
            });
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        });
        return command;
    }

    private static Command BuildCoverage()
    {
        var fileOption = FileOption();
        var command = new Command("coverage", "Compare JobML requirements with accepted, valid evidence")
        {
            fileOption
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var parsed = await ParseAsync(result.GetValue(fileOption)!, cancellationToken);
            var report = JobMlProcessor.AnalyseCoverage(parsed).Select(item => new
            {
                requirement = item.Requirement.Id,
                concept = item.Requirement.Concept,
                importance = item.Requirement.Importance,
                state = item.State.ToString().ToLowerInvariant(),
                claims = item.Claims.Select(claim => claim.Id)
            });
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        });
        return command;
    }

    private static Option<FileInfo> FileOption()
    {
        var option = new Option<FileInfo>("--file")
        {
            Required = true,
            Description = "Markdown document containing an embedded JobML block"
        };
        option.Aliases.Add("-f");
        return option;
    }

    private static async Task<JobMlFile> ParseAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(file.FullName, cancellationToken);
        var parser = new JobMlParser();
        if (!parser.TryParse(source, out var parsed, out var error))
            throw new InvalidDataException(error ?? "Invalid JobML document.");
        return parsed!;
    }
}
