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
using Mostlylucid.Avalonia.UITesting.Players;
using Mostlylucid.Avalonia.UITesting.Video;

namespace Mostlylucid.Avalonia.UITesting;

public sealed class UITestSession : IAsyncDisposable
{
    private readonly Window _window;
    private readonly object? _viewModel;
    private readonly string _screenshotDir;
    private readonly Action<string>? _log;
    private readonly UITestContext _context;
    private readonly Lazy<PointerSimulator> _pointer = new(() => new PointerSimulator());
    private GifRecorder? _activeVideoRecorder;

    public PointerSimulator Pointer => _pointer.Value;

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
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.MoveAsync(window, x, y);
        _log?.Invoke($"Mouse moved to: ({x}, {y})");
    }

    public async Task MouseDownAsync(double x, double y, MouseButton button = MouseButton.Left, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.DownAsync(window, x, y, button);
        _log?.Invoke($"Mouse down [{button}] at: ({x}, {y})");
    }

    public async Task MouseUpAsync(double x, double y, MouseButton button = MouseButton.Left, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.UpAsync(window, x, y, button);
        _log?.Invoke($"Mouse up [{button}] at: ({x}, {y})");
    }

    public async Task MouseClickAsync(double x, double y, MouseButton button = MouseButton.Left, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.ClickAsync(window, x, y, button);
        _log?.Invoke($"Mouse click [{button}] at: ({x}, {y})");
    }

    public async Task DragAsync(double x1, double y1, double x2, double y2, MouseButton button = MouseButton.Left, int steps = 10, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.DragAsync(window, x1, y1, x2, y2, steps, 16, button);
        _log?.Invoke($"Drag [{button}]: ({x1},{y1}) → ({x2},{y2})");
    }

    public async Task WheelAsync(double x, double y, double deltaX, double deltaY, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.WheelAsync(window, x, y, deltaX, deltaY);
        _log?.Invoke($"Wheel at ({x},{y}) Δ=({deltaX},{deltaY})");
    }

    public async Task PinchAsync(double x, double y, double totalScaleDelta, int steps = 10, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        for (int i = 0; i < steps; i++)
            await Pointer.MagnifyAsync(window, x, y, totalScaleDelta / steps);
        _log?.Invoke($"Pinch at ({x},{y}) Δ={totalScaleDelta}");
    }

    public async Task RotateAsync(double x, double y, double totalAngleDegrees, int steps = 10, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        for (int i = 0; i < steps; i++)
            await Pointer.RotateAsync(window, x, y, totalAngleDegrees / steps);
        _log?.Invoke($"Rotate at ({x},{y}) Δ={totalAngleDegrees}°");
    }

    public async Task SwipeAsync(double x, double y, double deltaX, double deltaY, int steps = 5, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        for (int i = 0; i < steps; i++)
            await Pointer.SwipeAsync(window, x, y, deltaX / steps, deltaY / steps);
        _log?.Invoke($"Swipe at ({x},{y}) Δ=({deltaX},{deltaY})");
    }

    public async Task TouchTapAsync(double x, double y, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.TouchTapAsync(window, x, y);
        _log?.Invoke($"Touch tap at ({x},{y})");
    }

    public async Task TouchDragAsync(double x1, double y1, double x2, double y2, int steps = 10, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await Pointer.TouchDragAsync(window, x1, y1, x2, y2, steps);
        _log?.Invoke($"Touch drag: ({x1},{y1}) → ({x2},{y2})");
    }

    // === Window operations ===

    public async Task ResizeWindowAsync(double width, double height, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => { window.Width = width; window.Height = height; });
        _log?.Invoke($"Window resized to {width}x{height}");
    }

    public async Task MoveWindowAsync(int x, int y, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => window.Position = new PixelPoint(x, y));
        _log?.Invoke($"Window moved to ({x},{y})");
    }

    public async Task SetWindowStateAsync(WindowState state, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => window.WindowState = state);
        _log?.Invoke($"Window state: {state}");
    }

    public Task MinimizeWindowAsync(string? windowId = null) => SetWindowStateAsync(WindowState.Minimized, windowId);
    public Task MaximizeWindowAsync(string? windowId = null) => SetWindowStateAsync(WindowState.Maximized, windowId);
    public Task RestoreWindowAsync(string? windowId = null) => SetWindowStateAsync(WindowState.Normal, windowId);
    public Task FullScreenWindowAsync(string? windowId = null) => SetWindowStateAsync(WindowState.FullScreen, windowId);

    public async Task FocusWindowAsync(string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => { window.Activate(); window.Focus(); });
        _log?.Invoke("Window focused");
    }

    public async Task CloseWindowAsync(string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => window.Close());
        _log?.Invoke("Window closed");
    }

    public async Task SetWindowTitleAsync(string title, string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        await RunOnUIThreadAsync(() => window.Title = title);
        _log?.Invoke($"Window title: {title}");
    }

    public async Task<(double Width, double Height, int X, int Y, WindowState State)> GetWindowInfoAsync(string? windowId = null)
    {
        var window = _context.FindWindow(windowId) ?? _window;
        return await RunOnUIThreadAsync(() =>
            (window.Width, window.Height, window.Position.X, window.Position.Y, window.WindowState));
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

    public async Task<string> SvgAsync(string? name = null, string? windowId = null)
    {
        var safeName = name ?? $"svg_{DateTime.UtcNow:HHmmss_fff}";
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.svg");

        var svgContent = await RunOnUIThreadAsync(() =>
        {
            var window = _context.FindWindow(windowId) ?? _window;
            window.UpdateLayout();
            var exporter = new Svg.SvgExporter();
            return exporter.Export(window);
        });

        await File.WriteAllTextAsync(filePath, svgContent);

        _log?.Invoke($"SVG: {filePath}");
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