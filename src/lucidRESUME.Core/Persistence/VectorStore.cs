using Microsoft.Data.Sqlite;

namespace lucidRESUME.Core.Persistence;

/// <summary>
/// Vector storage and KNN search over sqlite-vec.
/// Persists embeddings alongside metadata, enables fast similarity search.
///
/// At 10K vectors × 384 dimensions, brute-force KNN returns in &lt;5ms.
/// When sqlite-vec adds DiskANN support, this code stays the same - just faster.
/// </summary>
public sealed class VectorStore
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock;

    public VectorStore(SqliteConnection conn, SemaphoreSlim lockObj)
    {
        _conn = conn;
        _lock = lockObj;
    }

    /// <summary>Store a vector with metadata. Replaces if rowid exists.</summary>
    public async Task UpsertAsync(long rowId, float[] embedding,
        string sourceType, string? sourceId, string text, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Upsert into vec_embeddings
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO vec_embeddings(rowid, embedding) VALUES ($rowid, $embedding)";
            cmd.Parameters.AddWithValue("$rowid", rowId);
            cmd.Parameters.AddWithValue("$embedding", ToBlob(embedding));
            cmd.ExecuteNonQuery();

            // Upsert metadata
            using var metaCmd = _conn.CreateCommand();
            metaCmd.CommandText = "INSERT OR REPLACE INTO vec_meta(rowid, source_type, source_id, text) VALUES ($rowid, $type, $id, $text)";
            metaCmd.Parameters.AddWithValue("$rowid", rowId);
            metaCmd.Parameters.AddWithValue("$type", sourceType);
            metaCmd.Parameters.AddWithValue("$id", sourceId ?? (object)DBNull.Value);
            metaCmd.Parameters.AddWithValue("$text", text);
            metaCmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    /// <summary>Find K nearest neighbors to a query vector.</summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding, int k = 10, string? sourceTypeFilter = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var results = new List<VectorSearchResult>();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sourceTypeFilter != null
                ? """
                  SELECT v.rowid, v.distance, m.source_type, m.source_id, m.text
                  FROM vec_embeddings v
                  JOIN vec_meta m ON m.rowid = v.rowid
                  WHERE v.embedding MATCH $query
                    AND k = $k
                    AND m.source_type = $filter
                  ORDER BY v.distance
                  """
                : """
                  SELECT v.rowid, v.distance, m.source_type, m.source_id, m.text
                  FROM vec_embeddings v
                  JOIN vec_meta m ON m.rowid = v.rowid
                  WHERE v.embedding MATCH $query
                    AND k = $k
                  ORDER BY v.distance
                  """;

            cmd.Parameters.AddWithValue("$query", ToBlob(queryEmbedding));
            cmd.Parameters.AddWithValue("$k", k);
            if (sourceTypeFilter != null)
                cmd.Parameters.AddWithValue("$filter", sourceTypeFilter);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new VectorSearchResult
                {
                    RowId = reader.GetInt64(0),
                    Distance = reader.GetFloat(1),
                    SourceType = reader.GetString(2),
                    SourceId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Text = reader.GetString(4),
                });
            }

            return results;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Get the next available rowid.</summary>
    public async Task<long> NextRowIdAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(rowid), 0) + 1 FROM vec_meta";
            return (long)(cmd.ExecuteScalar() ?? 1L);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Count stored vectors.</summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM vec_meta";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Convert float[] to little-endian byte blob for sqlite-vec.</summary>
    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

public sealed class VectorSearchResult
{
    public long RowId { get; init; }
    public float Distance { get; init; }
    public string SourceType { get; init; } = "";
    public string? SourceId { get; init; }
    public string Text { get; init; } = "";

    /// <summary>Convert distance to similarity (1 - distance for L2, or direct for cosine).</summary>
    public float Similarity => 1f - Distance;
}