using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Persistence;

public sealed partial class SqliteAppStore
{
    private readonly ICareerProseEnricher? _proseEnricher;
    private readonly SemaphoreSlim _proseLock = new(1, 1);

    private async Task EnrichCareerAsync(CancellationToken ct)
    {
        if (_proseEnricher is null) return;
        await _proseLock.WaitAsync(ct);
        try
        {
            var source = (await LoadAsync(ct)).CompleteCareer?.Transcript;
            if (source is null) return;
            var sourceRevision = source.ProseIndex.SearchRevision;
            var previousEmbeddingRevision = source.ProseIndex.EmbeddingRevision;
            // Model inference never holds the SQLite lock. Readers and edits remain available.
            var analysed = await _proseEnricher.AnalyseAsync(source, ct);
            if (analysed.EmbeddingRevision == previousEmbeddingRevision) return;
            await _lock.WaitAsync(ct);
            try
            {
                var current = LoadCore();
                if (current.CompleteCareer?.Transcript is not { } transcript ||
                    transcript.ProseIndex.SearchRevision != sourceRevision) return; // A newer edit wins.
                transcript.ProseIndex = analysed;
                SaveCore(current);
            }
            finally { _lock.Release(); }
        }
        finally { _proseLock.Release(); }
    }

    private void SyncProseCore(CareerProseIndex? index)
    {
        var revision = index?.SearchRevision ?? "";
        if (LoadMetaJson<string>("career_prose_revision") == revision) return;
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = _conn.CreateCommand())
        {
            command.CommandText = "SELECT id, payload_hash FROM career_prose_chunks";
            using var rows = command.ExecuteReader();
            while (rows.Read()) existing[rows.GetString(0)] = rows.GetString(1);
        }
        var chunks = index?.Segments ?? [];
        var retained = chunks.Select(chunk => chunk.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in existing.Keys.Where(id => !retained.Contains(id))) RemoveChunk(removed);
        foreach (var chunk in chunks)
        {
            var keywords = string.Join(' ', chunk.TermFrequency.Keys.Concat(chunk.Skills.SelectMany(CareerProseLexicalIndex.Terms))
                .Select(SearchToken).Distinct(StringComparer.Ordinal));
            var payload = CareerProseLexicalIndex.Fingerprint(chunk.SubjectId + "|" + chunk.Text + "|" + keywords);
            if (existing.GetValueOrDefault(chunk.Id) == payload) continue;
            RemoveChunk(chunk.Id);
            using var command = _conn.CreateCommand();
            command.CommandText = """
                INSERT INTO career_prose_chunks(id, payload_hash) VALUES ($id, $hash);
                INSERT INTO career_prose_fts(segment_id, subject_id, text, keywords) VALUES ($id, $subject, $text, $keywords);
                """;
            command.Parameters.AddWithValue("$id", chunk.Id);
            command.Parameters.AddWithValue("$hash", payload);
            command.Parameters.AddWithValue("$subject", chunk.SubjectId);
            command.Parameters.AddWithValue("$text", chunk.Text);
            command.Parameters.AddWithValue("$keywords", keywords);
            command.ExecuteNonQuery();
        }
        UpsertMeta("career_prose_revision", System.Text.Json.JsonSerializer.Serialize(revision));
    }

    private void RemoveChunk(string id)
    {
        using var command = _conn.CreateCommand();
        command.CommandText = "DELETE FROM career_prose_fts WHERE segment_id = $id; DELETE FROM career_prose_chunks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static string SearchToken(string term) => term switch { "c#" => "csharp", "c++" => "cplusplus", ".net" => "dotnet", _ => term };

    public async Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default)
    {
        if (maximum <= 0) return [];
        // Only tokenizer-produced, quoted literals enter MATCH. Raw job text is never FTS syntax.
        var terms = CareerProseLexicalIndex.Terms(query).Select(SearchToken).Distinct(StringComparer.Ordinal).Take(48).ToArray();
        if (terms.Length == 0) return [];
        await _lock.WaitAsync(ct);
        try
        {
            if (LoadMetaJson<string>("career_prose_revision") != revision) return [];
            using var command = _conn.CreateCommand();
            command.CommandText = """
                SELECT segment_id, bm25(career_prose_fts, 0, 0, 1, 2) FROM career_prose_fts
                WHERE career_prose_fts MATCH $query ORDER BY 2, segment_id LIMIT $maximum;
                """;
            command.Parameters.AddWithValue("$query", string.Join(" OR ", terms.Select(term => "\"" + term + "\"")));
            command.Parameters.AddWithValue("$maximum", Math.Min(maximum, 256));
            var hits = new List<ProseSearchHit>();
            using var rows = await command.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct)) hits.Add(new(rows.GetString(0), -rows.GetDouble(1)));
            return hits;
        }
        finally { _lock.Release(); }
    }
}
