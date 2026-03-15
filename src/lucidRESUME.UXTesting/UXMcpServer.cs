using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace lucidRESUME.UXTesting;

public sealed class UXMcpServer
{
    private readonly UXContext _ctx;
    private readonly string _screenshotDir;
    private readonly int _port;
    private bool _running = true;
    
    public UXMcpServer(UXContext context, string screenshotDir = "ux-screenshots", int port = 0)
    {
        _ctx = context;
        _screenshotDir = screenshotDir;
        _port = port;
        Directory.CreateDirectory(screenshotDir);
    }
    
    public async Task RunStdioAsync()
    {
        Console.Error.WriteLine("UX MCP Server starting (stdio mode)...");
        
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        using var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
        
        await SendAsync(writer, new { jsonrpc = "2.0", method = "initialize", result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new { name = "ux-testing", version = "1.0.0" }
        }, id = 1 });
        
        while (_running)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            
            try
            {
                var request = JsonDocument.Parse(line);
                await HandleRequestAsync(writer, request);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }
    }
    
    private async Task HandleRequestAsync(StreamWriter writer, JsonDocument request)
    {
        var root = request.RootElement;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var id = root.TryGetProperty("id", out var i) ? i.GetInt32() : 0;
        
        if (method == "tools/list")
        {
            await SendAsync(writer, new
            {
                jsonrpc = "2.0",
                result = new
                {
                    tools = new object[]
                    {
                        Tool("ux_navigate", "Navigate to a page", ("page", "Page name")),
                        Tool("ux_click", "Click a control", ("name", "Control name")),
                        Tool("ux_type", "Type text into a control", ("name", "Control name"), ("text", "Text to type")),
                        Tool("ux_press", "Press a key", ("key", "Key name (Enter, Tab, etc)")),
                        Tool("ux_get", "Get a property value", ("path", "Property path")),
                        Tool("ux_set", "Set a property value", ("path", "Property path"), ("value", "Value")),
                        Tool("ux_screenshot", "Capture screenshot", ("name", "Optional filename")),
                        Tool("ux_describe", "Capture and describe screenshot", ("name", "Optional filename")),
                        Tool("ux_tree", "Get visual tree"),
                        Tool("ux_vm", "Get ViewModel properties"),
                        Tool("ux_wait", "Wait for milliseconds", ("ms", "Milliseconds")),
                        Tool("ux_waitfor", "Wait for property to equal value", ("path", "Property path"), ("value", "Expected value"), ("timeout", "Timeout ms")),
                        Tool("ux_assert", "Assert property equals value", ("path", "Property path"), ("value", "Expected value")),
                        Tool("ux_exit", "Exit the session")
                    }
                },
                id
            });
        }
        else if (method == "tools/call")
        {
            var toolName = root.GetProperty("params").GetProperty("name").GetString();
            var args = root.GetProperty("params").TryGetProperty("arguments", out var a) ? a : default;
            
            var result = await ExecuteToolAsync(toolName ?? "", args);
            
            await SendAsync(writer, new
            {
                jsonrpc = "2.0",
                result = new
                {
                    content = new[]
                    {
                        new { type = "text", text = result }
                    }
                },
                id
            });
        }
        else if (method == "initialize")
        {
            await SendAsync(writer, new
            {
                jsonrpc = "2.0",
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "ux-testing", version = "1.0.0" }
                },
                id
            });
        }
    }
    
    private async Task<string> ExecuteToolAsync(string tool, JsonElement args)
    {
        try
        {
            return tool switch
            {
                "ux_navigate" => await NavigateAsync(GetArg(args, "page")),
                "ux_click" => await ClickAsync(GetArg(args, "name")),
                "ux_type" => await TypeAsync(GetArg(args, "name"), GetArg(args, "text")),
                "ux_press" => await PressAsync(GetArg(args, "key")),
                "ux_get" => await GetAsync(GetArg(args, "path")),
                "ux_set" => await SetAsync(GetArg(args, "path"), GetArg(args, "value")),
                "ux_screenshot" => await ScreenshotAsync(GetArg(args, "name")),
                "ux_describe" => await DescribeAsync(GetArg(args, "name")),
                "ux_tree" => await GetTreeAsync(),
                "ux_vm" => await GetVmAsync(),
                "ux_wait" => await WaitAsync(GetArg(args, "ms")),
                "ux_waitfor" => await WaitForAsync(GetArg(args, "path"), GetArg(args, "value"), GetArg(args, "timeout")),
                "ux_assert" => await AssertAsync(GetArg(args, "path"), GetArg(args, "value")),
                "ux_exit" => Exit(),
                _ => $"Unknown tool: {tool}"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
    
    private static string GetArg(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return "";
        return args.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";
    }
    
    private async Task<string> NavigateAsync(string page)
    {
        await _ctx.RunOnUIThreadAsync(() => _ctx.Navigate?.Invoke(page));
        await Task.Delay(100);
        return $"Navigated to: {page}";
    }
    
    private async Task<string> ClickAsync(string name)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(name);
            if (control == null) return $"Control not found: {name}";
            
            if (control is Avalonia.Controls.Button button && button.Command?.CanExecute(button.CommandParameter) == true)
            {
                button.Command.Execute(button.CommandParameter);
                return $"Clicked: {name}";
            }
            
            control.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Input.InputElement.TappedEvent));
            return $"Tapped: {name}";
        });
    }
    
    private async Task<string> TypeAsync(string name, string text)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(name);
            if (control is Avalonia.Controls.TextBox textBox)
            {
                textBox.Text = text;
                return $"Typed into {name}: {text}";
            }
            return $"Control is not a TextBox: {name}";
        });
    }
    
    private async Task<string> PressAsync(string key)
    {
        var keyEnum = Enum.Parse<Avalonia.Input.Key>(key, true);
        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow?.RaiseEvent(new Avalonia.Input.KeyEventArgs
            {
                Key = keyEnum,
                RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent
            });
        });
        return $"Pressed: {key}";
    }
    
    private async Task<string> GetAsync(string path)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                var value = _ctx.GetProperty(vm, path);
                return $"{path} = {value ?? "null"}";
            }
            return "No DataContext";
        });
    }
    
    private async Task<string> SetAsync(string path, string value)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                if (_ctx.SetProperty(vm, path, value))
                    return $"Set {path} = {value}";
                return $"Failed to set {path}";
            }
            return "No DataContext";
        });
    }
    
    private async Task<string> ScreenshotAsync(string? name)
    {
        var safeName = name ?? $"screenshot_{DateTime.UtcNow:HHmmss}";
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        
        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow!.UpdateLayout();
            var size = new Avalonia.PixelSize((int)_ctx.MainWindow.Bounds.Width, (int)_ctx.MainWindow.Bounds.Height);
            var dpi = new Avalonia.Vector(96, 96);
            
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, dpi);
            bitmap.Render(_ctx.MainWindow);
            
            using var stream = File.Create(filePath);
            bitmap.Save(stream);
        });
        
        return filePath;
    }
    
    private async Task<string> DescribeAsync(string? name)
    {
        var screenshotPath = await ScreenshotAsync(name ?? "describe");
        
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "consoleimage",
            Arguments = $"--describe \"{screenshotPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        
        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return "Failed to start consoleimage";
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return output.Trim();
        }
        catch (Exception ex)
        {
            return $"Describe failed: {ex.Message}";
        }
    }
    
    private async Task<string> GetTreeAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow == null) return "No window";
            return BuildTree(_ctx.MainWindow, 0);
        });
    }
    
    private async Task<string> GetVmAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is not { } vm)
                return "No DataContext";
            
            var props = vm.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(p => $"  {p.Name}: {_ctx.GetProperty(vm, p.Name)}")
                .ToList();
            
            return $"ViewModel ({vm.GetType().Name}):\n" + string.Join("\n", props);
        });
    }
    
    private async Task<string> WaitAsync(string ms)
    {
        if (int.TryParse(ms, out var milliseconds))
        {
            await Task.Delay(milliseconds);
            return $"Waited {ms}ms";
        }
        return "Invalid milliseconds value";
    }
    
    private async Task<string> WaitForAsync(string path, string value, string timeout)
    {
        var timeoutMs = int.TryParse(timeout, out var t) ? t : 5000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var current = await _ctx.RunOnUIThreadAsync(() =>
            {
                if (_ctx.MainWindow?.DataContext is { } vm)
                    return _ctx.GetProperty(vm, path)?.ToString();
                return null;
            });
            
            if (current == value)
                return $"{path} = {value}";
            
            await Task.Delay(100);
        }
        
        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        
        return $"Timeout: {path} expected {value}, got {actual}";
    }
    
    private async Task<string> AssertAsync(string path, string value)
    {
        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        
        if (actual == value)
            return $"PASS: {path} = {value}";
        
        throw new Exception($"ASSERT FAILED: {path}\n  Expected: {value}\n  Actual: {actual}");
    }
    
    private string Exit()
    {
        _running = false;
        return "Exiting...";
    }
    
    private static string BuildTree(Avalonia.Controls.Control control, int depth)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrEmpty(control.Name) ? "" : $" #{control.Name}";
        var result = $"{indent}{control.GetType().Name}{name}\n";
        
        if (control is Avalonia.Controls.Panel panel)
        {
            foreach (var child in panel.Children.OfType<Avalonia.Controls.Control>())
                result += BuildTree(child, depth + 1);
        }
        else if (control is Avalonia.Controls.ContentControl cc && cc.Content is Avalonia.Controls.Control content)
        {
            result += BuildTree(content, depth + 1);
        }
        else if (control is Avalonia.Controls.Decorator d && d.Child is Avalonia.Controls.Control child)
        {
            result += BuildTree(child, depth + 1);
        }
        
        return result;
    }
    
    private static async Task SendAsync(StreamWriter writer, object response)
    {
        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json);
    }
    
    private static object Tool(string name, string description, params (string, string)[] args)
    {
        return new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties = args.ToDictionary(a => a.Item1, a => new { type = "string", description = a.Item2 }),
                required = args.Select(a => a.Item1).ToArray()
            }
        };
    }
}
