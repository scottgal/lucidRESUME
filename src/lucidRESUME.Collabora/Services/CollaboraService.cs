using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lucidRESUME.Collabora.Services;

public sealed class CollaboraService : IAsyncDisposable
{
    private readonly WopiHost _wopiHost;
    private readonly CollaboraOptions _options;
    private Process? _dockerProcess;
    private bool _disposed;
    private bool _isInitialized;

    public event EventHandler<string>? LogMessage;
    public bool IsRunning => _wopiHost.IsRunning;
    public string CodeUrl => _options.CodeUrl;
    public string WopiUrl => _wopiHost.HostUrl;

    public CollaboraService(CollaboraOptions options)
    {
        _options = options;
        _wopiHost = new WopiHost(_options.WopiPort, _options.CodeUrl);
        _wopiHost.LogMessage += (_, msg) => LogMessage?.Invoke(this, msg);
    }

    public async Task<bool> EnsureStartedAsync()
    {
        if (_isInitialized && _wopiHost.IsRunning)
            return true;

        try
        {
            if (_options.Enabled)
            {
                await EnsureCodeRunningAsync();
            }
            
            await _wopiHost.StartAsync();
            _isInitialized = true;
            LogMessage?.Invoke(this, "Collabora service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Failed to start Collabora service: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        await _wopiHost.StopAsync();
        StopCodeContainer();
        _isInitialized = false;
    }

    private async Task EnsureCodeRunningAsync()
    {
        if (await IsCodeRunningAsync())
        {
            LogMessage?.Invoke(this, "CODE container already running");
            return;
        }

        LogMessage?.Invoke(this, "Starting CODE container...");
        await StartCodeContainerAsync();
    }

    private async Task<bool> IsCodeRunningAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"{_options.CodeUrl}/hosting/discovery");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task StartCodeContainerAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && 
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LogMessage?.Invoke(this, "Docker not supported on this platform");
            return;
        }

        var containerName = "collabora-code-lucidresume";
        
        var stopArgs = $"stop {containerName} 2>nul || true";
        var removeArgs = $"rm -f {containerName} 2>nul || true";
        
        var startArgs = $"run -d --name {containerName} " +
                       $"-p 9980:9980 " +
                       $"-e \"extra_params=--o:ssl.enable=false --o:allowed_languages=en_US\" " +
                       $"collabora/code";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "docker",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                    ? $"/c docker {stopArgs} && docker {removeArgs} && docker {startArgs}"
                    : $"{stopArgs} && {removeArgs} && {startArgs}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _dockerProcess = Process.Start(psi);
            
            if (_dockerProcess != null)
            {
                await _dockerProcess.WaitForExitAsync();
                LogMessage?.Invoke(this, "CODE container started");
                
                await Task.Delay(3000);
                
                var retries = 10;
                while (retries > 0)
                {
                    if (await IsCodeRunningAsync())
                    {
                        LogMessage?.Invoke(this, "CODE is ready");
                        return;
                    }
                    await Task.Delay(1000);
                    retries--;
                }
                
                LogMessage?.Invoke(this, "CODE container started but not responding");
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Failed to start CODE container: {ex.Message}");
            LogMessage?.Invoke(this, "Make sure Docker is installed and running");
        }
    }

    private void StopCodeContainer()
    {
        if (_dockerProcess != null && !_dockerProcess.HasExited)
        {
            _dockerProcess.Kill();
            _dockerProcess.Dispose();
            _dockerProcess = null;
        }
    }

    public string RegisterFile(string filePath) => _wopiHost.RegisterFile(filePath);
    public void UnregisterFile(string fileId) => _wopiHost.UnregisterFile(fileId);
    public string GetEditorUrl(string fileId) => _wopiHost.GetEditorUrl(fileId);

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await StopAsync();
            await _wopiHost.DisposeAsync();
            _disposed = true;
        }
    }
}
