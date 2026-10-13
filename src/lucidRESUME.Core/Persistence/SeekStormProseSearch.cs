using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using SeekStorm.Bindings;
using SeekStorm.Bindings.Models;

namespace lucidRESUME.Core.Persistence;

/// <summary>Rebuildable native search over accepted canonical chunks. SQLite remains authoritative.</summary>
internal sealed class SeekStormProseSearch(string directory) : IDisposable
{
    private const string Policy = "seekstorm-0.2.4-engine-3.4.1-prose-v2";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SeekStormClient? _client;
    private Manifest? _manifest;
    private string ManifestPath => Path.Combine(directory, "manifest.json");

    public async Task PrepareAsync(CareerProseIndex index, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(() => Sync(index, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Invalidate(ex); }
        finally { _gate.Release(); }
    }

    private void Sync(CareerProseIndex index, CancellationToken ct)
    {
        var dimensions = string.IsNullOrWhiteSpace(index.EmbeddingSpace) ? 0 : index.Segments.Where(s => s.EmbeddingSpace == index.EmbeddingSpace && ValidVector(s.Embedding))
            .Select(s => s.Embedding.Length).FirstOrDefault(n => n > 0);
        var identity = Policy + "|" + (dimensions > 0 ? index.EmbeddingSpace : "lexical") + "|" + dimensions;
        Open();
        if (_manifest is null || _manifest.Identity != identity || _manifest.Revision.Length == 0)
        {
            Close();
            if (Directory.Exists(directory)) Directory.Move(directory, directory + ".old-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _client = new SeekStormClient();
            _client.CreateIndex(Path.Combine(directory, "index"), new IndexMeta
            {
                Name = "accepted-career-prose",
                Inference = dimensions > 0 ? SearchConfiguration.ExternalVectors(dimensions) : null
            }, [
                new() { Field = "key", FieldType = "String16", Store = true, Facet = true },
                new() { Field = "role", FieldType = "String16", Store = true, Facet = true },
                new() { Field = "kind", FieldType = "String16", Store = true, Facet = true },
                new() { Field = "terms", FieldType = "Text", IndexLexical = true },
                new() { Field = "vector", FieldType = "Json", IndexVector = dimensions > 0 }
            ]);
            _manifest = new() { Identity = identity, Dimensions = dimensions, EmbeddingSpace = index.EmbeddingSpace };
        }
        if (_manifest.Revision == index.SearchRevision && _manifest.EmbeddingRevision == index.EmbeddingRevision) return;
        var hashes = index.Segments.ToDictionary(s => s.Id, s => CareerProseLexicalIndex.Fingerprint(
            s.SubjectId + "|" + s.Kind + "|" + Terms(s.Text + " " + string.Join(' ', s.Skills)) + "|" +
            (dimensions > 0 && s.EmbeddingSpace == index.EmbeddingSpace && s.Embedding.Length == dimensions && ValidVector(s.Embedding)
                ? s.EmbeddingSpace + "|" + Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(s.Embedding.AsSpan()))) : "")));
        var changed = index.Segments.Where(s => _manifest.Chunks.GetValueOrDefault(s.Id) != hashes[s.Id]).ToList();
        var remove = _manifest.Chunks.Keys.Where(id => !hashes.ContainsKey(id) || _manifest.Chunks[id] != hashes[id]).ToArray();
        // A crash during native updates leaves an invalid manifest, requiring a rebuild.
        _manifest.Revision = "";
        WriteManifest();
        foreach (var keys in remove.Chunk(256))
        {
            ct.ThrowIfCancellationRequested();
            var hits = _client!.Search(new SearchRequest
            {
                EnableEmptyQuery = true,
                Length = 256,
                FacetFilter = new JsonArray((JsonNode)SearchConfiguration.StringFacet("key", keys)),
                Fields = ["key"]
            });
            _client.DeleteDocuments(hits.Results.Select(hit => hit.DocId).ToArray());
        }
        foreach (var batch in changed.Chunk(128))
        {
            ct.ThrowIfCancellationRequested();
            var documents = new JsonArray();
            foreach (var chunk in batch)
            {
                var document = new JsonObject
                {
                    ["key"] = chunk.Id,
                    ["role"] = chunk.SubjectId,
                    ["kind"] = chunk.Kind,
                    ["terms"] = Terms(chunk.Text + " " + string.Join(' ', chunk.Skills))
                };
                if (dimensions > 0 && chunk.EmbeddingSpace == index.EmbeddingSpace && chunk.Embedding.Length == dimensions && ValidVector(chunk.Embedding))
                    document["vector"] = JsonSerializer.SerializeToNode(chunk.Embedding);
                documents.Add((JsonNode)document);
            }
            _client!.IndexDocuments(documents.ToJsonString());
        }
        if (changed.Count > 0 || remove.Length > 0) _client!.Commit();
        _manifest.Chunks = hashes;
        _manifest.Revision = index.SearchRevision;
        _manifest.EmbeddingRevision = index.EmbeddingRevision;
        WriteManifest();
        foreach (var old in Directory.GetDirectories(Path.GetDirectoryName(directory)!, Path.GetFileName(directory) + ".old-*"))
            try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task<IReadOnlyList<ProseSearchHit>> SearchAsync(ProseSearchQuery query, CancellationToken ct)
    {
        if (query.Maximum <= 0) return [];
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run<IReadOnlyList<ProseSearchHit>>(() =>
            {
                Open();
                if (_client is null || _manifest?.Revision != query.Revision) return [];
                var text = string.Join(' ', Terms(query.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(48).Select(term => "\"" + term + "\""));
                if (text.Length == 0) return [];
                var hybrid = query.Embedding is { Length: > 0 } && query.Embedding.Length == _manifest.Dimensions &&
                    query.EmbeddingSpace == _manifest.EmbeddingSpace && ValidVector(query.Embedding);
                var filters = new JsonArray();
                if (query.SubjectId is not null) filters.Add((JsonNode)SearchConfiguration.StringFacet("role", query.SubjectId));
                if (query.Kind is not null) filters.Add((JsonNode)SearchConfiguration.StringFacet("kind", query.Kind));
                var result = _client.Search(new SearchRequest
                {
                    Query = text,
                    QueryType = "Union",
                    Length = Math.Min(256, query.Maximum),
                    SearchMode = hybrid ? "Hybrid" : "Lexical",
                    QueryVector = hybrid ? query.Embedding : null,
                    FacetFilter = filters.Count > 0 ? filters : null,
                    Fields = ["key", "role", "kind"]
                });
                // Defend the scope contract independently of native filtering.
                return result.Results.Where(hit =>
                        (query.SubjectId is null || hit.Document?["role"]?.GetValue<string>() == query.SubjectId) &&
                        (query.Kind is null || hit.Document?["kind"]?.GetValue<string>() == query.Kind))
                    .Select(hit => new ProseSearchHit(hit.Document!["key"]!.GetValue<string>(), hit.Score)).ToList();
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Invalidate(ex); return []; }
        finally { _gate.Release(); }
    }

    private void Open()
    {
        if (_client is not null || !File.Exists(ManifestPath)) return;
        try
        {
            _manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath));
            if (_manifest is null || _manifest.Revision.Length == 0) return;
            _client = new SeekStormClient();
            _client.OpenIndex(Path.Combine(directory, "index"));
        }
        catch (Exception error)
        {
            Close();
            _manifest = null;
            System.Diagnostics.Trace.TraceWarning("Rebuilding unreadable career search cache: {0}", error.Message);
        }
    }

    private void Invalidate(Exception error)
    {
        System.Diagnostics.Trace.TraceWarning("Career search will fall back to canonical evidence: {0}", error.Message);
        Close();
        if (_manifest is not null) { _manifest.Revision = ""; try { WriteManifest(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private void WriteManifest()
    {
        var temporary = ManifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_manifest));
        File.Move(temporary, ManifestPath, true);
    }

    private static string Terms(string text) => string.Join(' ', CareerProseLexicalIndex.Terms(text)
        .Select(term => term switch { "c#" => "csharp", "c++" => "cplusplus", ".net" => "dotnet", _ => term })
        );

    private void Close()
    {
        var client = _client;
        _client = null;
        try { client?.Dispose(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Closing career search cache: {0}", error.Message); }
    }

    private static bool ValidVector(float[] vector)
    {
        if (vector.Length == 0 || !vector.All(float.IsFinite)) return false;
        var norm = vector.Sum(value => value * value);
        return float.IsFinite(norm) && norm > 0;
    }

    public void Dispose() { Close(); _gate.Dispose(); }

    private sealed class Manifest
    {
        public string Identity { get; set; } = "";
        public string Revision { get; set; } = "";
        public string EmbeddingRevision { get; set; } = "";
        public string EmbeddingSpace { get; set; } = "";
        public int Dimensions { get; set; }
        public Dictionary<string, string> Chunks { get; set; } = new(StringComparer.Ordinal);
    }
}
