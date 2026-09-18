using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lucidRESUME.Core.Configuration;

namespace lucidRESUME.AI;

/// <summary>Locates and explicitly downloads the configured local GGUF model.</summary>
public sealed class LlamaSharpModelManager
{
    private readonly HttpClient _http;
    private readonly LlamaSharpOptions _options;
    private readonly ILogger<LlamaSharpModelManager> _logger;

    public LlamaSharpModelManager(
        HttpClient http,
        IOptions<LlamaSharpOptions> options,
        ILogger<LlamaSharpModelManager> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string ModelId => _options.ModelId;
    public string ModelPath => ResolveModelPath(_options.ModelPath);
    public bool IsModelPresent => File.Exists(ModelPath);

    public async Task DownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var destination = ModelPath;
        if (File.Exists(destination))
        {
            progress?.Report(1);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".download";

        try
        {
            using var response = await _http.GetAsync(
                _options.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(
                             partial,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    if (total is > 0)
                        progress?.Report((double)written / total.Value);
                }

                await target.FlushAsync(ct);
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(1);
            _logger.LogInformation("Downloaded {ModelId} to {Path}", ModelId, destination);
        }
        catch
        {
            if (File.Exists(partial))
                File.Delete(partial);
            throw;
        }
    }

    public static string ResolveModelPath(string configuredPath)
    {
        return AppDataPaths.Resolve(configuredPath);
    }
}
