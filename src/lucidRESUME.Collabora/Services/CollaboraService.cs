using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lucidRESUME.Collabora.Services;

public sealed class CollaboraService : IAsyncDisposable
{
    private const string ContainerName = "collabora-code-lucidresume";
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

        try
        {
            await RunDockerAsync(["stop", ContainerName], ignoreFailure: true);
            await RunDockerAsync(["rm", "-f", ContainerName], ignoreFailure: true);
            _dockerProcess = await RunDockerAsync([
                "run", "-d", "--name", ContainerName,
                "-p", "9980:9980",
                "-e", "extra_params=--o:ssl.enable=false --o:allowed_languages=en_US",
                "collabora/code"
            ]);

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

    private static async Task<Process?> RunDockerAsync(
        IEnumerable<string> arguments, bool ignoreFailure = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Docker could not be started.");
        await process.WaitForExitAsync();
        if (ignoreFailure)
        {
            process.Dispose();
            return null;
        }
        if (!ignoreFailure && process.ExitCode != 0)
        {
            var exitCode = process.ExitCode;
            var error = await process.StandardError.ReadToEndAsync();
            process.Dispose();
            throw new InvalidOperationException($"Docker failed with exit code {exitCode}: {error.Trim()}");
        }
        return process;
    }

    private void StopCodeContainer()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("stop");
            process.StartInfo.ArgumentList.Add(ContainerName);
            process.Start();
            process.WaitForExit(5000);
        }
        catch { /* Docker is optional and may already be stopped. */ }
        _dockerProcess?.Dispose();
        _dockerProcess = null;
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
