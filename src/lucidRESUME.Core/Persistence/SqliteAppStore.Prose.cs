using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Persistence;

public sealed partial class SqliteAppStore
{
    private readonly ICareerProseEnricher? _proseEnricher;
    private readonly SeekStormProseSearch _nativeProse;
    private readonly SemaphoreSlim _proseLock = new(1, 1);

    private async Task EnrichCareerAsync(CancellationToken ct)
    {
        await _proseLock.WaitAsync(ct);
        try
        {
            ResumeDocument? source;
            await _lock.WaitAsync(ct);
            try { source = LoadMetaJson<CompleteCareerRecord>("complete_career")?.Transcript; }
            finally { _lock.Release(); }
            if (source is null) return;
            await _nativeProse.PrepareAsync(source.ProseIndex, ct);
            if (_proseEnricher is null) return;
            var sourceRevision = source.ProseIndex.SearchRevision;
            var previousEmbeddingRevision = source.ProseIndex.EmbeddingRevision;
            // Model inference never holds the SQLite lock. Readers and edits remain available.
            var analysed = await _proseEnricher.AnalyseAsync(source, ct);
            if (analysed.EmbeddingRevision == previousEmbeddingRevision) return;
            await _lock.WaitAsync(ct);
            try
            {
                var current = LoadMetaJson<CompleteCareerRecord>("complete_career");
                if (current?.Transcript is not { } transcript ||
                    transcript.ProseIndex.SearchRevision != sourceRevision) return; // A newer edit wins.
                transcript.ProseIndex = analysed;
                // Updating derived analysis must not reload or reconcile archived resumes.
                UpsertMeta("complete_career", System.Text.Json.JsonSerializer.Serialize(current, JsonOpts));
            }
            finally { _lock.Release(); }
            await _nativeProse.PrepareAsync(analysed, ct);
        }
        finally { _proseLock.Release(); }
    }

    public async Task PrepareAsync(CareerProseIndex index, CancellationToken ct = default)
    {
        await _proseLock.WaitAsync(ct);
        try
        {
            await _lock.WaitAsync(ct);
            try
            {
                var revision = LoadMetaJson<string>("career_prose_revision");
                if (revision is null)
                {
                    // Upgrade only the canonical record; never rescan archived imports for search.
                    revision = LoadMetaJson<CompleteCareerRecord>("complete_career")?.Transcript.ProseIndex.SearchRevision;
                    if (revision is not null)
                        UpsertMeta("career_prose_revision", System.Text.Json.JsonSerializer.Serialize(revision));
                }
                if (revision != index.SearchRevision) return;
            }
            finally { _lock.Release(); }
            await _nativeProse.PrepareAsync(index, ct);
        }
        finally { _proseLock.Release(); }
    }

    public Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default)
        => SearchAsync(new ProseSearchQuery(revision, query, Maximum: maximum), ct);

    public async Task<IReadOnlyList<ProseSearchHit>> SearchAsync(ProseSearchQuery query, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { if (LoadMetaJson<string>("career_prose_revision") != query.Revision) return []; }
        finally { _lock.Release(); }
        return await _nativeProse.SearchAsync(query, ct);
    }
}
