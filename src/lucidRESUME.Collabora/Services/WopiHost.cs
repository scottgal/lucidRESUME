using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using lucidRESUME.Collabora.Models;

namespace lucidRESUME.Collabora.Services;

public sealed class WopiHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, WopiLock> _locks = new();
    private readonly ConcurrentDictionary<string, string> _filePaths = new();
    private readonly ConcurrentDictionary<string, string> _accessTokens = new();
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly string _codeUrl;
    private readonly string _hostUrl;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public string HostUrl => _hostUrl;
    public string CodeUrl => _codeUrl;
    public int Port => _port;
    public bool IsRunning { get; private set; }

    public event EventHandler<string>? LogMessage;

    public WopiHost(int port = 9981, string codeUrl = "http://localhost:9980")
    {
        _port = port;
        _codeUrl = codeUrl;
        _hostUrl = $"http://localhost:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        
        try
        {
            _listener.Start();
            IsRunning = true;
            LogMessage?.Invoke(this, $"WOPI Host started on port {_port}");
            
            _ = Task.Run(() => ListenAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Failed to start WOPI Host: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener.Stop();
        IsRunning = false;
        LogMessage?.Invoke(this, "WOPI Host stopped");
        
        await Task.CompletedTask;
    }

    public string RegisterFile(string filePath)
    {
        var fileId = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(filePath)))
            .Replace("/", "_").Replace("+", "-").TrimEnd('=');
        
        var accessToken = Guid.NewGuid().ToString("N");
        
        _accessTokens[fileId] = accessToken;
        _filePaths[fileId] = filePath;
        
        LogMessage?.Invoke(this, $"Registered file: {Path.GetFileName(filePath)} -> {fileId}");
        return fileId;
    }

    public void UnregisterFile(string fileId)
    {
        _accessTokens.TryRemove(fileId, out _);
        _filePaths.TryRemove(fileId, out _);
        _locks.TryRemove(fileId, out _);
    }

    public string GetAccessToken(string fileId)
    {
        return _accessTokens.TryGetValue(fileId, out var token) ? token : string.Empty;
    }

    public string GetEditorUrl(string fileId)
    {
        var token = GetAccessToken(fileId);
        var extension = GetFileExtension(fileId);
        return $"{_codeUrl}/browser/dist/cool.html?WOPISrc={Uri.EscapeDataString($"{_hostUrl}/wopi/files/{fileId}")}&access_token={token}";
    }

    private string GetFileExtension(string fileId)
    {
        var filePath = _filePaths.GetValueOrDefault(fileId);
        return string.IsNullOrEmpty(filePath) ? "docx" : Path.GetExtension(filePath).TrimStart('.');
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Listener error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        
        try
        {
            var path = request.Url?.AbsolutePath ?? "";
            
            LogMessage?.Invoke(this, $"{request.HttpMethod} {path}");
            
            if (path == "/wopi/discovery")
            {
                await HandleDiscoveryAsync(response);
            }
            else if (path.StartsWith("/wopi/files/"))
            {
                var segments = path.Split('/');
                var fileId = segments.Length > 3 ? segments[3] : "";
                var action = segments.Length > 4 ? segments[4] : "";

                if (!ValidateAccessToken(request, fileId))
                {
                    response.StatusCode = 401;
                    return;
                }

                if (action == "contents")
                {
                    if (request.HttpMethod == "GET")
                        await HandleGetFileAsync(response, fileId);
                    else if (request.HttpMethod == "POST")
                        await HandlePutFileAsync(request, response, fileId);
                }
                else
                {
                    if (request.HttpMethod == "GET")
                        await HandleCheckFileInfoAsync(response, fileId);
                    else if (request.HttpMethod == "POST")
                        await HandleLockOperationAsync(request, response, fileId);
                }
            }
            else
            {
                response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Request error: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    private async Task HandleDiscoveryAsync(HttpListenerResponse response)
    {
        var discoveryXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<wopi-discovery>
    <net-zone name=""internal-http"">
        <app name=""Word"">
            <action name=""view"" ext=""doc"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""doc"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""view"" ext=""docx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""docx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
        </app>
        <app name=""Excel"">
            <action name=""view"" ext=""xls"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""xls"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""view"" ext=""xlsx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""xlsx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
        </app>
        <app name=""PowerPoint"">
            <action name=""view"" ext=""ppt"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""ppt"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""view"" ext=""pptx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
            <action name=""edit"" ext=""pptx"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
        </app>
        <app name=""Word"">
            <action name=""view"" ext=""pdf"" urlsrc=""{_codeUrl}/browser/dist/cool.html?WOPISrc="" />
        </app>
    </net-zone>
</wopi-discovery>";

        response.ContentType = "application/xml";
        response.ContentLength64 = Encoding.UTF8.GetByteCount(discoveryXml);
        await using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(discoveryXml);
    }

    private async Task HandleCheckFileInfoAsync(HttpListenerResponse response, string fileId)
    {
        var filePath = _filePaths.GetValueOrDefault(fileId);
        
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            response.StatusCode = 404;
            return;
        }

        var fileInfo = new FileInfo(filePath);
        
        var info = new WopiCheckFileInfo
        {
            BaseFileName = fileInfo.Name,
            Size = fileInfo.Length,
            FileExtension = fileInfo.Extension.TrimStart('.'),
            LastModifiedTime = fileInfo.LastWriteTimeUtc.ToString("o"),
            OwnerId = "lucidRESUME",
            UserId = "user",
            UserFriendlyName = "User",
            ReadOnly = false,
            UserCanWrite = true
        };

        var json = JsonConvert.SerializeObject(info);
        response.ContentType = "application/json";
        response.ContentLength64 = Encoding.UTF8.GetByteCount(json);
        await using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(json);
    }

    private async Task HandleGetFileAsync(HttpListenerResponse response, string fileId)
    {
        var filePath = _filePaths.GetValueOrDefault(fileId);
        
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            response.StatusCode = 404;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        response.ContentType = "application/octet-stream";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private async Task HandlePutFileAsync(HttpListenerRequest request, HttpListenerResponse response, string fileId)
    {
        var filePath = _filePaths.GetValueOrDefault(fileId);
        
        if (string.IsNullOrEmpty(filePath))
        {
            response.StatusCode = 404;
            return;
        }

        using var ms = new MemoryStream();
        await request.InputStream.CopyToAsync(ms);
        await File.WriteAllBytesAsync(filePath, ms.ToArray());

        var json = JsonConvert.SerializeObject(new { Size = ms.Length });
        response.ContentType = "application/json";
        response.ContentLength64 = Encoding.UTF8.GetByteCount(json);
        await using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(json);
    }

    private async Task HandleLockOperationAsync(HttpListenerRequest request, HttpListenerResponse response, string fileId)
    {
        var lockHeader = request.Headers["X-WOPI-Lock"];
        var lockOp = request.Headers["X-WOPI-Override"];

        switch (lockOp)
        {
            case "LOCK":
                _locks[fileId] = new WopiLock
                {
                    LockId = lockHeader ?? Guid.NewGuid().ToString(),
                    ExpiresAt = DateTime.UtcNow.AddHours(24)
                };
                break;
            case "UNLOCK":
                _locks.TryRemove(fileId, out _);
                break;
            case "REFRESH_LOCK":
                if (_locks.TryGetValue(fileId, out var existingLock))
                {
                    existingLock.ExpiresAt = DateTime.UtcNow.AddHours(24);
                }
                break;
        }

        response.StatusCode = 200;
        await response.OutputStream.WriteAsync(Array.Empty<byte>());
    }

    private bool ValidateAccessToken(HttpListenerRequest request, string fileId)
    {
        var query = request.Url?.Query;
        if (string.IsNullOrEmpty(query)) return false;
        
        var queryParams = QueryStringParser.Parse(query);
        var token = queryParams.GetValueOrDefault("access_token");
        var expectedToken = _accessTokens.GetValueOrDefault(fileId);
        return !string.IsNullOrEmpty(token) && token == expectedToken;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await StopAsync();
            _listener.Close();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}
