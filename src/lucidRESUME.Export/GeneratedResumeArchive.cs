using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Configuration;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.Export;

public sealed record GeneratedResumeArchivePath(string Directory);

/// <summary>Freezes the resume and evidence together, and stores exports outside the source career corpus.</summary>
public sealed class GeneratedResumeArchive(IAppStore store, IEnumerable<IResumeExporter> exporters,
    GeneratedResumeArchivePath? path = null)
{
    public string Root { get; } = path?.Directory ?? Path.Combine(AppDataPaths.Root, "generated-resumes");
    public string DirectoryFor(Guid id) => Path.Combine(Root, id.ToString("N"));
    public string FileFor(Guid id, string extension) => Path.Combine(DirectoryFor(id), "resume." + extension);

    public async Task<GeneratedResume> SaveAsync(ResumeDocument source, ResumeDocument resume, JobDescription job,
        CancellationToken ct = default)
    {
        // Serialization deliberately detaches mutable UI state and strips transient aggregate helpers.
        var entry = JsonSerializer.Deserialize<GeneratedResume>(JsonSerializer.Serialize(new GeneratedResume
        { Source = source, Resume = resume, Job = job }))!;
        var directory = DirectoryFor(entry.Id);
        Directory.CreateDirectory(directory);
        try
        {
            await EnsureFilesAsync(entry, ct);
            await store.MutateAsync(state => state.GeneratedResumes.Add(entry), ct);
            return entry;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public async Task EnsureFilesAsync(GeneratedResume entry, CancellationToken ct = default)
    {
        var directory = DirectoryFor(entry.Id);
        Directory.CreateDirectory(directory);
        foreach (var format in new[] { ExportFormat.Docx, ExportFormat.Pdf })
        {
            var extension = format == ExportFormat.Docx ? "docx" : "pdf";
            var file = FileFor(entry.Id, extension);
            if (File.Exists(file)) continue;
            var exporter = exporters.Single(item => item.Format == format);
            var bytes = await Task.Run(() => exporter.ExportAsync(entry.Resume, ct), ct);
            await AtomicWriteAsync(file, bytes, ct);
        }
        var markdown = FileFor(entry.Id, "jobml.md");
        if (!File.Exists(markdown))
            await AtomicWriteAsync(markdown, Encoding.UTF8.GetBytes(entry.Resume.JobMlSource ?? entry.Resume.RawMarkdown ?? ""), ct);
        var evidence = Path.Combine(directory, "career-evidence.json");
        if (!File.Exists(evidence))
            await AtomicWriteAsync(evidence, JsonSerializer.SerializeToUtf8Bytes(entry.Source), ct);
        if (PdfPages(entry.Id).Length == 0)
        {
            var pages = await Task.Run(() => new PdfExporter().RenderPages(entry.Resume), ct);
            for (var i = 0; i < pages.Count; i++)
                await AtomicWriteAsync(Path.Combine(directory, $"pdf-page-{i + 1:D3}.png"), pages[i], ct);
        }
    }

    public string[] PdfPages(Guid id) => Directory.Exists(DirectoryFor(id))
        ? Directory.GetFiles(DirectoryFor(id), "pdf-page-*.png").Order(StringComparer.Ordinal).ToArray() : [];

    private static async Task AtomicWriteAsync(string file, byte[] bytes, CancellationToken ct)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
