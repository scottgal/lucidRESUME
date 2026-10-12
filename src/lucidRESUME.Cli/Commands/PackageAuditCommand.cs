using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

public static class PackageAuditCommand
{
    public static Command Build()
    {
        var publisher = new Option<string>("--publisher")
        { Required = true, Description = "NuGet publisher/profile search term" };
        var output = new Option<FileInfo?>("--output")
        { Description = "Optional JSON audit output" };
        var config = new Option<FileInfo?>("--config") { Description = "Path to lucidresume.json config" };
        var command = new Command("package-audit",
            "Audit public NuGet packages and group them into searchable product families")
        {
            publisher, output, config
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            await using var services = ServiceBootstrap.Build(result.GetValue(config)?.FullName);
            var audit = await services.GetRequiredService<NuGetPackageAuditService>()
                .AuditAsync(result.GetValue(publisher)!, cancellationToken);
            Console.WriteLine($"{audit.PackageCount} packages, {audit.Families.Count} families, {audit.TotalDownloads:N0} cumulative downloads");
            foreach (var family in audit.Families)
                Console.WriteLine($"  {family.Name,-28} {family.PackageCount,3} packages  {family.TotalDownloads,12:N0} downloads  {family.SourceRepository ?? "registry metadata"}");
            var target = result.GetValue(output);
            if (target is not null)
                await File.WriteAllTextAsync(target.FullName,
                    JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        });
        return command;
    }
}
