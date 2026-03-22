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
using Mostlylucid.Avalonia.UITesting.Video;

namespace Mostlylucid.Avalonia.UITesting;

public sealed class UITestSession : IAsyncDisposable
{
    private readonly Window _window;
    private readonly object? _viewModel;
    private readonly string _screenshotDir;
    private readonly Action<string>? _log;
    private readonly UITestContext _context;
    private GifRecorder? _activeVideoRecorder;

    public Window Window => _window;
    public object? ViewModel => _viewModel;
    public UITestContext Context => _context;

    internal UITestSession(Window window, object? viewModel, string screenshotDir, Action<string>? log, UITestContext? context = null)
    {
        _window = window;
        _viewModel = viewModel;
        _screenshotDir = screenshotDir;
        _log = log;
        _context = context ?? new UITestContext { MainWindow = window };
        Directory.CreateDirectory(screenshotDir);
    }

    public static Task<UITestSession> AttachAsync(Window window, Action<UITestSessionOptions>? configure = null)
    {
        var options = new UITestSessionOptions();
        configure?.Invoke(options);

        var context = new UITestContext
        {
            MainWindow = window,
            Navigate = options.NavigateAction
        };

        if (options.EnableCrossWindowTracking)
            context.EnableCrossWindowTracking();

        var viewModel = window.DataContext;
        var session = new UITestSession(window, viewModel, options.ScreenshotDir, options.Log, context);
        return Task.FromResult(session);
    }

    public async Task NavigateAsync(string page)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (_context.Navigate != null)
            {
                _context.Navigate(page);
            }
            else
            {
                var navProp = _viewModel?.GetType().GetProperty("NavigateCommand");
                var cmd = navProp?.GetValue(_viewModel);
                cmd?.GetType().GetMethod("Execute")?.Invoke(cmd, new object[] { page });
            }
        });
        await Task.Delay(100);
        _log?.Invoke($"Navigated to: {page}");
    }

    public async Task ClickAsync(string controlName, string? windowId = null)
    {
        await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            var control = _context.FindControl(controlName, window);
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

    public async Task DoubleClickAsync(string controlName, string? windowId = null)
    {
        await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            var control = _context.FindControl(controlName, window);
            if (control == null) throw new InvalidOperationException($"Control not found: {controlName}");
            control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
        });
        await Task.Delay(50);
        _log?.Invoke($"Double-clicked: {controlName}");
    }

    public async Task TypeAsync(string controlName, string text, string? windowId = null)
    {
        await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            var control = _context.FindControl(controlName, window);
            if (control is TextBox textBox)
            {
                textBox.Text = text;
            }
        });
        await Task.Delay(50);
        _log?.Invoke($"Typed into {controlName}: {text}");
    }

    public async Task PressAsync(string key, string? controlName = null, string? windowId = null)
    {
        var keyEnum = Enum.Parse<Key>(key, true);
        await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            var target = controlName != null ? _context.FindControl(controlName, window) : window;
            target?.RaiseEvent(new KeyEventArgs
            {
                Key = keyEnum,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        await Task.Delay(50);
        _log?.Invoke($"Pressed: {key}");
    }

    public async Task MouseMoveAsync(double x, double y, string? windowId = null)
    {
        // Mouse move is best-effort — Avalonia's PointerEventArgs constructor is internal.
        // Log intent for script replay; actual replay uses ScriptPlayer coordinates.
        _log?.Invoke($"Mouse moved to: ({x}, {y})");
        await Task.CompletedTask;
    }

    public async Task<T?> GetPropertyAsync<T>(string path)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var value = _context.GetProperty(_viewModel!, path);
            if (value == null) return default;
            return (T)Convert.ChangeType(value, typeof(T));
        });
    }

    public async Task SetPropertyAsync<T>(string path, T value)
    {
        await RunOnUIThreadAsync(() => _context.SetProperty(_viewModel!, path, value));
        _log?.Invoke($"Set {path} = {value}");
    }

    public async Task<string> ScreenshotAsync(string? name = null, string? windowId = null)
    {
        var safeName = name ?? $"screenshot_{DateTime.UtcNow:HHmmss_fff}";
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");

        await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            window.UpdateLayout();
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            var dpi = new Vector(96, 96);

            using var bitmap = new RenderTargetBitmap(size, dpi);
            bitmap.Render(window);

            using var stream = File.Create(filePath);
            bitmap.Save(stream);
        });

        _log?.Invoke($"Screenshot: {filePath}");
        return filePath;
    }

    public async Task<GifRecorder> StartVideoAsync(int fps = 5, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        var recorder = new GifRecorder(fps, _log);
        recorder.StartRecording(window);
        _activeVideoRecorder = recorder;
        _log?.Invoke($"Video recording started at {fps} fps");
        return recorder;
    }

    public async Task<string> StopVideoAsync(string? name = null)
    {
        if (_activeVideoRecorder == null)
            throw new InvalidOperationException("No video recording in progress");

        await _activeVideoRecorder.StopRecordingAsync();

        var safeName = name ?? $"video_{DateTime.UtcNow:HHmmss_fff}";
        var gifPath = Path.Combine(_screenshotDir, $"{safeName}.gif");
        await _activeVideoRecorder.SaveAsync(gifPath);

        // Try MP4 too
        var mp4Path = Path.Combine(_screenshotDir, $"{safeName}.mp4");
        await _activeVideoRecorder.TryExportMp4Async(mp4Path);

        await _activeVideoRecorder.DisposeAsync();
        _activeVideoRecorder = null;

        return gifPath;
    }

    public async Task<string> GetTreeAsync(string? windowId = null)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            return BuildTree(window, 0);
        });
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
            var value = await RunOnUIThreadAsync(() => _context.GetProperty(_viewModel!, path));
            if (Equals(value, expectedValue) || value?.ToString() == expectedValue?.ToString())
                return;

            await Task.Delay(100);
        }

        var actual = await RunOnUIThreadAsync(() => _context.GetProperty(_viewModel!, path));
        throw new TimeoutException($"WaitForProperty timeout: {path} expected {expectedValue}, got {actual}");
    }

    public async Task AssertPropertyAsync(string path, object expectedValue)
    {
        var actual = await RunOnUIThreadAsync(() => _context.GetProperty(_viewModel!, path));

        if (!Equals(actual, expectedValue) && actual?.ToString() != expectedValue?.ToString())
        {
            throw new UITestAssertException($"Assert failed: {path}\n  Expected: {expectedValue}\n  Actual: {actual}");
        }
    }

    public async Task<ControlInfo[]> GetControlsAsync(string? windowId = null)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            var controls = new List<ControlInfo>();
            FindControlsRecursive(window, controls);
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
        if (_activeVideoRecorder != null)
            await _activeVideoRecorder.DisposeAsync();
    }
}

public class UITestSessionOptions
{
    public string ScreenshotDir { get; set; } = "ux-screenshots";
    public Action<string>? Log { get; set; }
    public Action<string>? NavigateAction { get; set; }
    public bool EnableCrossWindowTracking { get; set; } = true;
}

public record ControlInfo(string Name, string Type, Rect Bounds);

public class UITestAssertException : Exception
{
    public UITestAssertException(string message) : base(message) { }
}
