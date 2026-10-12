using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Local, content-addressed variants of source prose, including skills and provenance.</summary>
public sealed class ReusableProseStore(string directory)
{
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<ReusableProseAccount?> FindAsync(string fingerprint, CancellationToken ct)
    {
        var path = Path.Combine(directory, fingerprint + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            var account = JsonSerializer.Deserialize<ReusableProseAccount>(await File.ReadAllTextAsync(path, ct));
            return account?.Key == fingerprint ? account : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public async Task SaveAsync(ReusableProseAccount account, CancellationToken ct)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, account.Key + ".json");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(account), ct);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Reuse must not block generation. */ }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }
}
