using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using lucidRESUME.UXTesting.Scripts;

namespace lucidRESUME.UXTesting;

public sealed class UXRepl
{
    private readonly UXContext _ctx;
    private readonly string _screenshotDir;
    private bool _running = true;
    
    public UXRepl(UXContext context, string screenshotDir = "ux-screenshots")
    {
        _ctx = context;
        _screenshotDir = screenshotDir;
        Directory.CreateDirectory(screenshotDir);
    }
    
    public async Task RunAsync()
    {
        Console.WriteLine("UX Testing REPL - type 'help' for commands");
        Console.WriteLine($"Window: {_ctx.MainWindow?.Title ?? "not set"}");
        
        while (_running)
        {
            Console.Write("ux> ");
            var line = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            try
            {
                var result = await ExecuteCommandAsync(line);
                if (!string.IsNullOrEmpty(result))
                    Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
    
    public async Task<string> ExecuteCommandAsync(string line)
    {
        var parts = SplitCommand(line);
        if (parts.Length == 0) return "";
        
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();
        
        return cmd switch
        {
            "help" => GetHelp(),
            "exit" or "quit" => Exit(),
            "nav" => await NavAsync(args),
            "click" => await ClickAsync(args),
            "type" => await TypeAsync(args),
            "press" => await PressAsync(args),
            "get" => await GetAsync(args),
            "set" => await SetAsync(args),
            "list" => await ListAsync(args),
            "tree" => await TreeAsync(args),
            "screenshot" or "shot" => await ScreenshotAsync(args),
            "wait" => await WaitAsync(args),
            "assert" => await AssertAsync(args),
            "run" => await RunAsync(args),
            "vm" => await VmAsync(args),
            "service" or "svc" => Service(args),
            "describe" or "desc" => await DescribeAsync(args),
            "waitfor" => await WaitForAsync(args),
            _ => $"Unknown command: {cmd}. Type 'help' for commands."
        };
    }
    
    private static string GetHelp()
    {
        return """
            Commands:
              nav <page>              Navigate to page (uses Navigate action)
              click <control>         Click a control by name
              type <control> <text>   Type text into a TextBox
              press <key>             Press a key (Enter, Tab, Escape, etc.)
              get <path>              Get property (e.g., get CurrentPage.Name)
              set <path> <value>      Set property value
              list [controls|vms]     List controls or view models
              tree                    Show visual tree
              vm                      Show current VM properties
              screenshot [name]       Capture screenshot
              describe [name]         Capture and AI describe screenshot
              wait <ms>               Wait for milliseconds
              waitfor <path> <value>  Wait until property equals value
              assert <path> <value>   Assert property equals value
              run <script.yaml>       Run a script file
              service <type>          Get a service from DI container
              exit                    Exit REPL
            """;
    }
    
    private string Exit()
    {
        _running = false;
        return "Exiting...";
    }
    
    private async Task<string> NavAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: nav <page>";
        
        var page = string.Join(" ", args);
        await _ctx.RunOnUIThreadAsync(() => _ctx.Navigate?.Invoke(page));
        await Task.Delay(100);
        return $"Navigated to: {page}";
    }
    
    private async Task<string> ClickAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: click <control>";
        
        var controlName = args[0];
        var result = await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(controlName);
            if (control == null) return $"Control not found: {controlName}";
            
            if (control is Button button)
            {
                if (button.Command?.CanExecute(button.CommandParameter) == true)
                {
                    button.Command.Execute(button.CommandParameter);
                    return $"Clicked button: {controlName}";
                }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return $"Raised click on: {controlName}";
            }
            
            if (control is TabItem tabItem)
            {
                tabItem.IsSelected = true;
                return $"Selected tab: {controlName}";
            }
            
            control.RaiseEvent(new RoutedEventArgs(InputElement.TappedEvent));
            return $"Tapped: {controlName}";
        });
        
        await Task.Delay(50);
        return result;
    }
    
    private async Task<string> TypeAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: type <control> <text>";
        
        var controlName = args[0];
        var text = string.Join(" ", args.Skip(1));
        
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(controlName);
            if (control is TextBox textBox)
            {
                textBox.Text = text;
                return $"Typed into {controlName}: {text}";
            }
            return $"Control is not a TextBox: {controlName}";
        });
    }
    
    private async Task<string> PressAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: press <key>";
        
        var keyName = args[0];
        var key = Enum.Parse<Key>(keyName, true);
        
        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow?.RaiseEvent(new KeyEventArgs
            {
                Key = key,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        
        await Task.Delay(50);
        return $"Pressed: {keyName}";
    }
    
    private async Task<string> GetAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: get <path> (e.g., get CurrentPage.Name or get #MyControl.Text)";
        
        var path = string.Join(" ", args);
        
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (path.StartsWith("#"))
            {
                var dotIdx = path.IndexOf('.');
                var controlName = dotIdx > 0 ? path[1..dotIdx] : path[1..];
                var propPath = dotIdx > 0 ? path[(dotIdx + 1)..] : "";
                
                var control = _ctx.FindControl(controlName);
                if (control == null) return $"Control not found: {controlName}";
                
                if (string.IsNullOrEmpty(propPath)) return $"{control}";
                return $"{propPath} = {_ctx.GetProperty(control, propPath)}";
            }
            
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                var value = _ctx.GetProperty(vm, path);
                return $"{path} = {value}";
            }
            
            return "No DataContext available";
        });
    }
    
    private async Task<string> SetAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: set <path> <value>";
        
        var path = args[0];
        var value = string.Join(" ", args.Skip(1));
        
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                if (_ctx.SetProperty(vm, path, value))
                    return $"Set {path} = {value}";
                return $"Failed to set {path}";
            }
            return "No DataContext available";
        });
    }
    
    private async Task<string> ListAsync(string[] args)
    {
        var type = args.Length > 0 ? args[0].ToLowerInvariant() : "controls";
        
        return type switch
        {
            "vms" or "viewmodels" => await ListViewModelsAsync(),
            _ => await ListControlsAsync()
        };
    }
    
    private async Task<string> ListControlsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var controls = _ctx.GetAllControls()
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => $"  {c.Name} ({c.GetType().Name})")
                .ToList();
            
            if (controls.Count == 0)
                return "No named controls found";
            
            return $"Controls ({controls.Count}):\n" + string.Join("\n", controls);
        });
    }
    
    private async Task<string> ListViewModelsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is not { } vm)
                return "No DataContext";
            
            var props = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.Name.EndsWith("ViewModel"))
                .Select(p => $"  {p.Name}: {p.PropertyType.Name}")
                .ToList();
            
            if (props.Count == 0)
                return "No ViewModel properties found";
            
            return "ViewModels:\n" + string.Join("\n", props);
        });
    }
    
    private async Task<string> TreeAsync(string[] args)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow == null) return "No window";
            return BuildTree(_ctx.MainWindow, 0);
        });
    }
    
    private static string BuildTree(Control control, int depth)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrEmpty(control.Name) ? "" : $" #{control.Name}";
        var result = $"{indent}{control.GetType().Name}{name}\n";
        
        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
                result += BuildTree(child, depth + 1);
        }
        else if (control is ContentControl cc && cc.Content is Control content)
        {
            result += BuildTree(content, depth + 1);
        }
        else if (control is Decorator d && d.Child is Control child)
        {
            result += BuildTree(child, depth + 1);
        }
        
        return result;
    }
    
    private async Task<string> VmAsync(string[] args)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is not { } vm)
                return "No DataContext";
            
            var props = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"  {p.Name}: {_ctx.GetProperty(vm, p.Name)}")
                .ToList();
            
            return $"ViewModel ({vm.GetType().Name}):\n" + string.Join("\n", props);
        });
    }
    
    private string Service(string[] args)
    {
        if (args.Length == 0) return "Usage: service <TypeName>";
        
        var typeName = string.Join(" ", args);
        var service = _ctx.Services?.GetService(Type.GetType(typeName) ?? 
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == typeName || t.FullName == typeName));
        
        return service?.ToString() ?? $"Service not found: {typeName}";
    }
    
    private async Task<string> ScreenshotAsync(string[] args)
    {
        var name = args.Length > 0 ? args[0] : $"screenshot_{DateTime.UtcNow:HHmmss}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        
        var result = await CaptureScreenshotAsync(filePath);
        return $"Screenshot saved: {result}";
    }
    
    private async Task<string> WaitAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: wait <ms>";
        
        var ms = int.Parse(args[0]);
        await Task.Delay(ms);
        return $"Waited {ms}ms";
    }
    
    private async Task<string> AssertAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: assert <path> <expected>";
        
        var path = args[0];
        var expected = string.Join(" ", args.Skip(1));
        
        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        
        if (actual == expected)
            return $"PASS: {path} = {expected}";
        
        throw new Exception($"ASSERT FAILED: {path}\n  Expected: {expected}\n  Actual: {actual}");
    }
    
    private async Task<string> RunAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: run <script.yaml>";
        
        var scriptPath = args[0];
        if (!File.Exists(scriptPath)) return $"Script not found: {scriptPath}";
        
        var yaml = await File.ReadAllTextAsync(scriptPath);
        var script = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .Build()
            .Deserialize<UXScript>(yaml);
        
        foreach (var action in script.Actions)
        {
            var cmd = action.Type switch
            {
                ActionType.Navigate => $"nav {action.Value}",
                ActionType.Click => $"click {action.Target}",
                ActionType.TypeText => $"type {action.Target} {action.Value}",
                ActionType.PressKey => $"press {action.Value}",
                ActionType.Wait => $"wait {action.Value}",
                ActionType.Screenshot => $"screenshot {action.Value}",
                _ => null
            };
            
            if (cmd != null)
            {
                Console.WriteLine($"  > {cmd}");
                await ExecuteCommandAsync(cmd);
            }
            
            var delay = action.DelayMs > 0 ? action.DelayMs : script.DefaultDelay;
            if (delay > 0) await Task.Delay(delay);
        }
        
        return $"Script completed: {script.Name}";
    }
    
    private async Task<string> CaptureScreenshotAsync(string filePath)
    {
        if (_ctx.MainWindow == null) return "No window";
        
        var width = (int)_ctx.MainWindow.Bounds.Width;
        var height = (int)_ctx.MainWindow.Bounds.Height;
        
        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow!.UpdateLayout();
            
            var size = new PixelSize(width, height);
            var dpi = new Vector(96, 96);
            
            using var bitmap = new RenderTargetBitmap(size, dpi);
            bitmap.Render(_ctx.MainWindow);
            
            using var stream = File.Create(filePath);
            bitmap.Save(stream);
        });
        
        return filePath;
    }
    
    private async Task<string> DescribeAsync(string[] args)
    {
        var name = args.Length > 0 ? args[0] : $"describe_{DateTime.UtcNow:HHmmss}";
        var screenshotResult = await ScreenshotAsync(new[] { name });
        var filePath = Path.Combine(_screenshotDir, $"{string.Join("_", name.Split(Path.GetInvalidFileNameChars()))}.png");
        
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "consoleimage",
            Arguments = $"\"{filePath}\" --max-width 80 --max-height 25",
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
            
            return $"\n{output}";
        }
        catch (Exception ex)
        {
            return $"Describe failed (is consoleimage installed?): {ex.Message}";
        }
    }
    
    private async Task<string> WaitForAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: waitfor <path> <value> [timeout_ms]";
        
        var path = args[0];
        var expected = args[1];
        var timeoutMs = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 5000;
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var actual = await _ctx.RunOnUIThreadAsync(() =>
            {
                if (_ctx.MainWindow?.DataContext is { } vm)
                    return _ctx.GetProperty(vm, path)?.ToString();
                return null;
            });
            
            if (actual == expected)
                return $"{path} = {expected}";
            
            await Task.Delay(100);
        }
        
        var final = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        
        return $"Timeout: {path} expected {expected}, got {final}";
    }
    
    private static string[] SplitCommand(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = "";
                }
            }
            else
            {
                current += c;
            }
        }
        
        if (current.Length > 0)
            result.Add(current);
        
        return result.ToArray();
    }
}
