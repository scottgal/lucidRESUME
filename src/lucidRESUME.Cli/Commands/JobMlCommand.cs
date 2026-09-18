using System.CommandLine;
using System.Text.Json;
using lucidRESUME.JobML;

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
