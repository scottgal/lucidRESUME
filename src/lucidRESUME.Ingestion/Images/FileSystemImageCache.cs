using System.Security.Cryptography;
using System.Text;
using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Ingestion.Images;

/// <summary>
/// Stores page PNG images under:
///   {baseDir}/{cacheKey}/page-{n:D3}.png
///
/// Default base dir: %AppData%/lucidRESUME/images/
/// </summary>
public sealed class FileSystemImageCache : IDocumentImageCache
{
    private readonly string _baseDir;

    public FileSystemImageCache(string? baseDir = null)
    {
        _baseDir = baseDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "lucidRESUME", "images");
    }

    public string ComputeKey(string filePath)
    {
        var info = new FileInfo(filePath);
        var raw = $"{filePath}:{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public string? GetCachedPagePath(string cacheKey, int pageNumber)
    {
        var path = PagePath(cacheKey, pageNumber);
        return File.Exists(path) ? path : null;
    }

    public async Task<string> StorePageAsync(string cacheKey, int pageNumber, byte[] pngBytes, CancellationToken ct = default)
    {
        var dir = Path.Combine(_baseDir, cacheKey);
        Directory.CreateDirectory(dir);
        var path = PagePath(cacheKey, pageNumber);
        await File.WriteAllBytesAsync(path, pngBytes, ct);
        return path;
    }

    public bool IsFullyCached(string cacheKey, int pageCount)
    {
        for (var i = 1; i <= pageCount; i++)
            if (!File.Exists(PagePath(cacheKey, i))) return false;
        return pageCount > 0;
    }

    private string PagePath(string cacheKey, int pageNumber) =>
        Path.Combine(_baseDir, cacheKey, $"page-{pageNumber:D3}.png");
}
