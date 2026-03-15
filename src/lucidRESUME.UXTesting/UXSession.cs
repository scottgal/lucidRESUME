using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace lucidRESUME.UXTesting;

public sealed class UXSession : IAsyncDisposable
{
    private readonly Window _window;
    private readonly object? _viewModel;
    private readonly string _screenshotDir;
    private readonly Action<string>? _log;
    private Process? _mcpProcess;
    
    public Window Window => _window;
    public object? ViewModel => _viewModel;
    
    internal UXSession(Window window, object? viewModel, string screenshotDir, Action<string>? log)
    {
        _window = window;
        _viewModel = viewModel;
        _screenshotDir = screenshotDir;
        _log = log;
        Directory.CreateDirectory(screenshotDir);
    }
    
    public static async Task<UXSession> LaunchAsync(string assemblyPath, string[]? args = null, Action<UXSessionOptions>? configure = null)
    {
        var options = new UXSessionOptions();
        configure?.Invoke(options);
        
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{assemblyPath} --ux-headless {(args != null ? string.Join(" ", args) : "")}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        
        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException($"Failed to launch: {assemblyPath}");
        
        await Task.Delay(2000);
        
        throw new NotImplementedException("Headless session not yet implemented - use AttachAsync instead");
    }
    
    public static Task<UXSession> AttachAsync(Window window, Action<UXSessionOptions>? configure = null)
    {
        var options = new UXSessionOptions();
        configure?.Invoke(options);
        
        var viewModel = window.DataContext;
        var session = new UXSession(window, viewModel, options.ScreenshotDir, options.Log);
        return Task.FromResult(session);
    }
    
    public async Task NavigateAsync(string page)
    {
        await RunOnUIThreadAsync(() =>
        {
            var navProp = _viewModel?.GetType().GetProperty("NavigateCommand");
            var cmd = navProp?.GetValue(_viewModel);
            cmd?.GetType().GetMethod("Execute")?.Invoke(cmd, new object[] { page });
        });
        await Task.Delay(100);
        _log?.Invoke($"Navigated to: {page}");
    }
    
    public async Task ClickAsync(string controlName)
    {
        await RunOnUIThreadAsync(() =>
        {
            var control = FindControl(controlName);
            if (control == null) throw new InvalidOperationException($"Control not found: {controlName}");
            
            if (control is Button button && button.Command?.CanExecute(button.CommandParameter) == true)
            {
                button.Command.Execute(button.CommandParameter);
            }
            else if (control is TabItem tabItem)
            {
                tabItem.IsSelected = true;
            }
            else
            {
                control.RaiseEvent(new RoutedEventArgs(InputElement.TappedEvent));
            }
        });
        await Task.Delay(50);
        _log?.Invoke($"Clicked: {controlName}");
    }
    
    public async Task TypeAsync(string controlName, string text)
    {
        await RunOnUIThreadAsync(() =>
        {
            var control = FindControl(controlName);
            if (control is TextBox textBox)
            {
                textBox.Text = text;
            }
        });
        await Task.Delay(50);
        _log?.Invoke($"Typed into {controlName}: {text}");
    }
    
    public async Task PressAsync(string key)
    {
        var keyEnum = Enum.Parse<Key>(key, true);
        await RunOnUIThreadAsync(() =>
        {
            _window.RaiseEvent(new KeyEventArgs
            {
                Key = keyEnum,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        await Task.Delay(50);
        _log?.Invoke($"Pressed: {key}");
    }
    
    public async Task<T?> GetPropertyAsync<T>(string path)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var value = GetPropertyValue(path);
            if (value == null) return default;
            return (T)Convert.ChangeType(value, typeof(T));
        });
    }
    
    public async Task SetPropertyAsync<T>(string path, T value)
    {
        await RunOnUIThreadAsync(() => SetPropertyValue(path, value));
        _log?.Invoke($"Set {path} = {value}");
    }
    
    public async Task<string> ScreenshotAsync(string? name = null)
    {
        var safeName = name ?? $"screenshot_{DateTime.UtcNow:HHmmss_fff}";
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        
        await RunOnUIThreadAsync(() =>
        {
            _window.UpdateLayout();
            var size = new PixelSize((int)_window.Bounds.Width, (int)_window.Bounds.Height);
            var dpi = new Vector(96, 96);
            
            using var bitmap = new RenderTargetBitmap(size, dpi);
            bitmap.Render(_window);
            
            using var stream = File.Create(filePath);
            bitmap.Save(stream);
        });
        
        _log?.Invoke($"Screenshot: {filePath}");
        return filePath;
    }
    
    public async Task<string> DescribeAsync(string? name = null)
    {
        var screenshotPath = await ScreenshotAsync(name ?? "describe");
        
        var psi = new ProcessStartInfo
        {
            FileName = "consoleimage",
            Arguments = $"--describe \"{screenshotPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        
        try
        {
            using var process = Process.Start(psi);
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
    
    public async Task<string> GetTreeAsync()
    {
        return await RunOnUIThreadAsync(() => BuildTree(_window, 0));
    }
    
    public async Task<IDictionary<string, string>> GetViewModelPropertiesAsync()
    {
        return await RunOnUIThreadAsync(() =>
        {
            var result = new Dictionary<string, string>();
            if (_viewModel == null) return result;
            
            foreach (var prop in _viewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var value = prop.GetValue(_viewModel);
                    result[prop.Name] = value?.ToString() ?? "null";
                }
                catch
                {
                    result[prop.Name] = "<error>";
                }
            }
            
            return result;
        });
    }
    
    public async Task WaitForPropertyAsync(string path, object expectedValue, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var value = await RunOnUIThreadAsync(() => GetPropertyValue(path));
            if (Equals(value, expectedValue) || value?.ToString() == expectedValue?.ToString())
                return;
            
            await Task.Delay(100);
        }
        
        var actual = await RunOnUIThreadAsync(() => GetPropertyValue(path));
        throw new TimeoutException($"WaitForProperty timeout: {path} expected {expectedValue}, got {actual}");
    }
    
    public async Task AssertPropertyAsync(string path, object expectedValue)
    {
        var actual = await RunOnUIThreadAsync(() => GetPropertyValue(path));
        
        if (!Equals(actual, expectedValue) && actual?.ToString() != expectedValue?.ToString())
        {
            throw new AssertException($"Assert failed: {path}\n  Expected: {expectedValue}\n  Actual: {actual}");
        }
    }
    
    public async Task<ControlInfo[]> GetControlsAsync()
    {
        return await RunOnUIThreadAsync(() =>
        {
            var controls = new List<ControlInfo>();
            FindControlsRecursive(_window, controls);
            return controls.ToArray();
        });
    }
    
    private void FindControlsRecursive(Control control, List<ControlInfo> result)
    {
        if (!string.IsNullOrEmpty(control.Name))
        {
            result.Add(new ControlInfo(control.Name, control.GetType().Name, control.Bounds));
        }
        
        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
                FindControlsRecursive(child, result);
        }
        else if (control is ContentControl cc && cc.Content is Control content)
        {
            FindControlsRecursive(content, result);
        }
        else if (control is Decorator d && d.Child is Control child)
        {
            FindControlsRecursive(child, result);
        }
    }
    
    private Control? FindControl(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_window.Name == name) return _window;
        return _window.FindControl<Control>(name);
    }
    
    private object? GetPropertyValue(string path)
    {
        var parts = path.Split('.');
        object? current = _viewModel;
        
        foreach (var part in parts)
        {
            if (current == null) return null;
            
            var type = current.GetType();
            var prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        
        return current;
    }
    
    private void SetPropertyValue(string path, object? value)
    {
        var parts = path.Split('.');
        if (parts.Length == 0) return;
        
        object? current = _viewModel;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return;
            var prop = current.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return;
            current = prop.GetValue(current);
        }
        
        if (current == null) return;
        
        var finalProp = current.GetType().GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (finalProp == null || !finalProp.CanWrite) return;
        
        var convertedValue = Convert.ChangeType(value, finalProp.PropertyType);
        finalProp.SetValue(current, convertedValue);
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
    
    private async Task RunOnUIThreadAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }
    
    private async Task<T> RunOnUIThreadAsync<T>(Func<T> func)
    {
        return await Dispatcher.UIThread.InvokeAsync(func);
    }
    
    public async ValueTask DisposeAsync()
    {
        _mcpProcess?.Kill();
        await Task.CompletedTask;
    }
}

public class UXSessionOptions
{
    public string ScreenshotDir { get; set; } = "ux-screenshots";
    public Action<string>? Log { get; set; }
}

public record ControlInfo(string Name, string Type, Rect Bounds);

public class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}
