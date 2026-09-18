using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Infrastructure;

internal static class ResumeOutputWriter
{
    public static async Task WriteAsync(
        IServiceProvider services,
        ResumeDocument artifact,
        string format,
        FileInfo? output,
        CancellationToken ct)
    {
        if (format.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (output is null) throw new ArgumentException("--output is required when --format all is used.");
            var basePath = Path.HasExtension(output.FullName)
                ? Path.Combine(output.DirectoryName!, Path.GetFileNameWithoutExtension(output.Name))
                : output.FullName;
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            await WriteFormatAsync(services, artifact, ExportFormat.Markdown, basePath + ".md", ct);
            await WriteFormatAsync(services, artifact, ExportFormat.Docx, basePath + ".docx", ct);
            await WriteFormatAsync(services, artifact, ExportFormat.Pdf, basePath + ".pdf", ct);
            return;
        }

        var exportFormat = format.ToLowerInvariant() switch
        {
            "docx" => ExportFormat.Docx,
            "pdf" => ExportFormat.Pdf,
            _ => ExportFormat.Markdown
        };
        var exporter = services.GetServices<IResumeExporter>().First(item => item.Format == exportFormat);
        var bytes = await exporter.ExportAsync(artifact, ct);
        if (output is null)
        {
            await Console.OpenStandardOutput().WriteAsync(bytes, ct);
            return;
        }
        await File.WriteAllBytesAsync(output.FullName, bytes, ct);
        Console.Error.WriteLine($"Written {exportFormat.ToString().ToUpperInvariant()} to {output.FullName}");
    }

    private static async Task WriteFormatAsync(
        IServiceProvider services,
        ResumeDocument artifact,
        ExportFormat format,
        string path,
        CancellationToken ct)
    {
        var exporter = services.GetServices<IResumeExporter>().First(item => item.Format == format);
        await File.WriteAllBytesAsync(path, await exporter.ExportAsync(artifact, ct), ct);
        Console.Error.WriteLine($"Written {format.ToString().ToUpperInvariant()} to {path}");
    }
}
