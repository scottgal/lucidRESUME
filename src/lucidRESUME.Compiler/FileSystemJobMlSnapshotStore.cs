using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Compiler;

/// <summary>
/// Publishes immutable, validated snapshots of a JobML career-record projection.
/// Reads never run extraction or an LLM.
/// </summary>
public sealed class FileSystemJobMlSnapshotStore(IOptions<JobMlCompilerOptions> options) : IJobMlSnapshotStore
{
    private readonly string _directory = Path.GetFullPath(options.Value.SnapshotDirectory);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JobMlParser _parser = new();

    public async Task<JobMlSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var pointer = Path.Combine(_directory, "current.json");
        if (!File.Exists(pointer)) return null;
        var json = await File.ReadAllTextAsync(pointer, cancellationToken);
        var value = JsonSerializer.Deserialize<SnapshotPointer>(json);
        return value is null ? null : await GetAsync(value.Revision, cancellationToken);
    }

    public async Task<JobMlSnapshot?> GetAsync(string revision, CancellationToken cancellationToken = default)
    {
        if (!IsRevision(revision)) return null;
        var documentPath = Path.Combine(_directory, $"{revision}.md");
        var metadataPath = Path.Combine(_directory, $"{revision}.json");
        if (!File.Exists(documentPath) || !File.Exists(metadataPath)) return null;
        var source = await File.ReadAllTextAsync(documentPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<SnapshotPointer>(
            await File.ReadAllTextAsync(metadataPath, cancellationToken));
        if (metadata is null) return null;
        var file = _parser.Parse(source);
        return new JobMlSnapshot(revision, metadata.PublishedAt, source, file, JobMlProcessor.Validate(file));
    }

    public async Task<JobMlSnapshot> PublishAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var file = _parser.Parse(source);
        var diagnostics = JobMlProcessor.Validate(file);
        var blocking = diagnostics.Where(d => d.Severity == JobMlDiagnosticSeverity.Error).ToList();
        var acceptedDrift = JobMlProcessor.Reconcile(file).Any(resolution =>
            string.Equals(resolution.Claim.Review, "accepted", StringComparison.OrdinalIgnoreCase) &&
            resolution.Evidence.Any(e => e.State is EvidenceState.Changed or EvidenceState.Missing or EvidenceState.Ambiguous));
        if (blocking.Count > 0)
            throw new JobMlPublicationException(string.Join(" ", blocking.Select(d => $"{d.Code}: {d.Message}")));
        if (acceptedDrift)
            throw new JobMlPublicationException(
                "An accepted claim relies on changed, missing, or ambiguous evidence. Review it before publishing a new master snapshot.");

        var normalized = _parser.Serialize(file);
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var publishedAt = DateTimeOffset.UtcNow;
        var pointer = new SnapshotPointer(revision, publishedAt);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            await AtomicWriteAsync(Path.Combine(_directory, $"{revision}.md"), normalized, cancellationToken);
            await AtomicWriteAsync(Path.Combine(_directory, $"{revision}.json"), JsonSerializer.Serialize(pointer), cancellationToken);
            await AtomicWriteAsync(Path.Combine(_directory, "current.json"), JsonSerializer.Serialize(pointer), cancellationToken);
        }
        finally { _gate.Release(); }

        return new JobMlSnapshot(revision, publishedAt, normalized, file, diagnostics);
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static bool IsRevision(string revision) =>
        revision.Length == 64 && revision.All(Uri.IsHexDigit);

    private sealed record SnapshotPointer(string Revision, DateTimeOffset PublishedAt);
}
