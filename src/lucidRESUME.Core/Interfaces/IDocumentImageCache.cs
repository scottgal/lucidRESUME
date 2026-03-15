namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Stores and retrieves rendered page images for a converted document.
/// Cache key is derived from the source file (path + mtime hash) so the
/// same file always hits the same slot without re-processing.
/// </summary>
public interface IDocumentImageCache
{
    /// <summary>Returns the filesystem path for a cached page, or null if not yet cached.</summary>
    string? GetCachedPagePath(string cacheKey, int pageNumber);

    /// <summary>Writes page PNG bytes to the cache and returns the path.</summary>
    Task<string> StorePageAsync(string cacheKey, int pageNumber, byte[] pngBytes, CancellationToken ct = default);

    /// <summary>True if all <paramref name="pageCount"/> pages are already cached.</summary>
    bool IsFullyCached(string cacheKey, int pageCount);

    /// <summary>Computes a stable cache key for a source file.</summary>
    string ComputeKey(string filePath);
}
