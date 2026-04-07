using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mostlylucid.Avalonia.UITesting.Scripts;
using Mostlylucid.Avalonia.UITesting.Video;

namespace Mostlylucid.Avalonia.UITesting.Players;

public sealed class ScriptPlayer
{
    private readonly string _screenshotDir;
    private readonly int _defaultDelay;
    private readonly bool _captureScreenshots;
    private Window? _window;
    private Action<string>? _navigateAction;
    private readonly UITestContext _context;
    private GifRecorder? _videoRecorder;

    public event EventHandler<string>? Log;
    public event EventHandler<UIActionResult>? ActionCompleted;

    public ScriptPlayer(string screenshotDir, int defaultDelay = 200, bool captureScreenshots = true, UITestContext? context = null)
    {
        _screenshotDir = screenshotDir;
        _defaultDelay = defaultDelay;
        _captureScreenshots = captureScreenshots;
        _context = context ?? new UITestContext();
        Directory.CreateDirectory(screenshotDir);
    }

    public void SetNavigateAction(Action<string> navigate)
    {
        _navigateAction = navigate;
        _context.Navigate = navigate;
    }

    public async Task<UITestResult> RunScriptAsync(Window window, UIScript script)
    {
        _window = window;
        _context.MainWindow = window;
        _context.EnableCrossWindowTracking();

        var result = new UITestResult
        {
            ScriptName = script.Name,
            StartTime = DateTime.UtcNow
        };

        Log?.Invoke(this, $"Running script: {script.Name}");
        Log?.Invoke(this, $"Actions: {script.Actions.Count}");

        try
        {
            foreach (var action in script.Actions)
            {
                var actionResult = await ExecuteActionAsync(action, script.DefaultDelay);
                result.ActionResults.Add(actionResult);

                ActionCompleted?.Invoke(this, actionResult);

                if (!actionResult.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Action failed: {action.Type} - {actionResult.ErrorMessage}";
                    break;
                }
            }

            result.Success = result.ActionResults.All(a => a.Success);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log?.Invoke(this, $"Error: {ex.Message}");
        }

        // Stop any active video recording
        if (_videoRecorder != null)
        {
            await _videoRecorder.StopRecordingAsync();
            var videoPath = Path.Combine(_screenshotDir, $"{script.Name}_video.gif");
            await _videoRecorder.SaveAsync(videoPath);
            result.VideoPath = videoPath;
            await _videoRecorder.DisposeAsync();
            _videoRecorder = null;
        }

        result.EndTime = DateTime.UtcNow;

        Log?.Invoke(this, $"Completed: {result.Duration.TotalSeconds:F2}s");

        return result;
    }

    private async Task<UIActionResult> ExecuteActionAsync(UIAction action, int defaultDelay)
    {
        var sw = Stopwatch.StartNew();
        var result = new UIActionResult
        {
            Action = action,
            Success = false
        };

        try
        {
            Log?.Invoke(this, $"  [{action.Type}] {action.Target ?? ""} {action.Value ?? ""}");

            switch (action.Type)
            {
                case ActionType.Navigate:
                    await ExecuteNavigateAsync(action);
                    break;
                case ActionType.Click:
                    await ExecuteClickAsync(action);
                    break;
                case ActionType.DoubleClick:
                    await ExecuteDoubleClickAsync(action);
                    break;
                case ActionType.RightClick:
                    await ExecuteRightClickAsync(action);
                    break;
                case ActionType.TypeText:
                    await ExecuteTypeTextAsync(action);
                    break;
                case ActionType.PressKey:
                    await ExecutePressKeyAsync(action);
                    break;
                case ActionType.Hover:
                    await ExecuteHoverAsync(action);
                    break;
                case ActionType.Scroll:
                    await ExecuteScrollAsync(action);
                    break;
                case ActionType.Wait:
                    await ExecuteWaitAsync(action);
                    break;
                case ActionType.Screenshot:
                    await ExecuteScreenshotAsync(action, result);
                    break;
                case ActionType.Assert:
                    await ExecuteAssertAsync(action);
                    break;
                case ActionType.MouseMove:
                    await ExecuteMouseMoveAsync(action);
                    break;
                case ActionType.MouseDown:
                    await ExecuteMouseDownAsync(action);
                    break;
                case ActionType.MouseUp:
                    await ExecuteMouseUpAsync(action);
                    break;
                case ActionType.StartVideo:
                    await ExecuteStartVideoAsync(action);
                    break;
                case ActionType.StopVideo:
                    await ExecuteStopVideoAsync(action, result);
                    break;
                case ActionType.Svg:
                    await ExecuteSvgAsync(action, result);
                    break;
                default:
                    throw new NotSupportedException($"Action type {action.Type} not supported");
            }

            result.Success = true;

            var delay = action.DelayMs > 0 ? action.DelayMs : defaultDelay;
            if (delay > 0)
            {
                await Task.Delay(delay);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            Log?.Invoke(this, $"    Failed: {ex.Message}");
        }

        sw.Stop();
        result.Duration = sw.Elapsed;

        if (_captureScreenshots && action.Type != ActionType.Screenshot && action.Type != ActionType.Wait)
        {
            result.ScreenshotPath = await CaptureScreenshotAsync($"{action.Type}_{DateTime.UtcNow:HHmmss_fff}", action.WindowId);
        }

        return result;
    }

    private async Task ExecuteNavigateAsync(UIAction action)
    {
        if (_navigateAction == null || action.Value == null) return;

        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            _navigateAction(action.Value);
            tcs.SetResult();
        }, DispatcherPriority.Normal);
        await tcs.Task;
        await Task.Delay(300);
    }

    private async Task ExecuteClickAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window);
        if (control == null)
            throw new InvalidOperationException($"Control not found: {action.Target}");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
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
    }

    private async Task ExecuteDoubleClickAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window);
        if (control == null)
            throw new InvalidOperationException($"Control not found: {action.Target}");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
        });
        await Task.Delay(50);
    }

    private async Task ExecuteRightClickAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window);
        if (control == null)
            throw new InvalidOperationException($"Control not found: {action.Target}");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.RaiseEvent(new RoutedEventArgs(InputElement.TappedEvent));
        });
        await Task.Delay(50);
    }

    private async Task ExecuteTypeTextAsync(UIAction action)
    {
        if (string.IsNullOrEmpty(action.Value)) return;

        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (control is TextBox textBox)
            {
                textBox.Text = action.Value;
            }
        });
        await Task.Delay(50);
    }

    private async Task ExecutePressKeyAsync(UIAction action)
    {
        if (string.IsNullOrEmpty(action.Value)) return;

        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window) ?? window;
        var key = Enum.Parse<Key>(action.Value, true);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control?.RaiseEvent(new KeyEventArgs
            {
                Key = key,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        await Task.Delay(50);
    }

    private async Task ExecuteHoverAsync(UIAction action)
    {
        // Hover is recorded for intent - Avalonia doesn't expose PointerEventArgs constructors publicly.
        // The action is logged and can trigger hover-sensitive bindings via automation peers in future.
        Log?.Invoke(this, $"    Hover: {action.Target} (logged for replay)");
        await Task.Delay(50);
    }

    private async Task ExecuteScrollAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);

        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            ScrollViewer? sv = action.Target != null
                ? window?.FindControl<ScrollViewer>(action.Target)
                : FindFirstScrollViewer(window);

            if (sv != null)
            {
                var value = action.Value?.ToLowerInvariant()?.Trim() ?? "down";
                switch (value)
                {
                    case "top": sv.Offset = sv.Offset.WithY(0); break;
                    case "bottom": sv.Offset = sv.Offset.WithY(sv.Extent.Height); break;
                    case "up": sv.PageUp(); break;
                    case "down": sv.PageDown(); break;
                    case "pageup": sv.PageUp(); break;
                    case "pagedown": sv.PageDown(); break;
                    case "lineup": sv.LineUp(); break;
                    case "linedown": sv.LineDown(); break;
                    default:
                        // Percentage: "50%" scrolls to 50% of content
                        if (value.EndsWith('%') && double.TryParse(value.TrimEnd('%'), out var pct))
                        {
                            sv.Offset = sv.Offset.WithY(sv.Extent.Height * pct / 100.0);
                        }
                        // Pixel offset: "300" scrolls to 300px, "+200" scrolls down 200px
                        else if (double.TryParse(action.Value, out var px))
                        {
                            if (action.Value!.StartsWith('+') || action.Value.StartsWith('-'))
                                sv.Offset = sv.Offset.WithY(sv.Offset.Y + px);
                            else
                                sv.Offset = sv.Offset.WithY(px);
                        }
                        else
                            sv.PageDown();
                        break;
                }
            }
            tcs.SetResult();
        }, DispatcherPriority.Normal);
        await tcs.Task;
        await Task.Delay(100);
    }

    private async Task ExecuteWaitAsync(UIAction action)
    {
        var ms = int.TryParse(action.Value, out var v) ? v : action.DelayMs;
        if (ms > 0)
        {
            await Task.Delay(ms);
        }
    }

    private async Task ExecuteScreenshotAsync(UIAction action, UIActionResult result)
    {
        var name = action.Value ?? $"screenshot_{DateTime.UtcNow:HHmmss_fff}";
        result.ScreenshotPath = await CaptureScreenshotAsync(name, action.WindowId);
    }

    private async Task ExecuteAssertAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);
        var control = FindControl(action.Target, window);
        if (control == null)
            throw new InvalidOperationException($"Assert failed: Control not found: {action.Target}");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (action.Value?.StartsWith("visible:") == true)
            {
                var expected = action.Value["visible:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                if (control.IsVisible != expected)
                    throw new UITestAssertException($"Assert failed: {action.Target}.IsVisible != {expected}");
            }
            else if (action.Value?.StartsWith("enabled:") == true)
            {
                var expected = action.Value["enabled:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                if (control.IsEnabled != expected)
                    throw new UITestAssertException($"Assert failed: {action.Target}.IsEnabled != {expected}");
            }
            else if (action.Value?.StartsWith("text:") == true && control is TextBox textBox)
            {
                var expected = action.Value["text:".Length..].Trim();
                if (textBox.Text != expected)
                    throw new UITestAssertException($"Assert failed: {action.Target}.Text != {expected}");
            }
        });
    }

    private async Task ExecuteMouseMoveAsync(UIAction action)
    {
        // Mouse move positions are recorded for documentation/replay logging.
        // Avalonia doesn't expose public PointerEventArgs constructors.
        Log?.Invoke(this, $"    MouseMove: ({action.X}, {action.Y})");
        await Task.CompletedTask;
    }

    private async Task ExecuteMouseDownAsync(UIAction action)
    {
        // MouseDown at coordinates - logged for replay intent.
        // For actual click behavior, use Click action with control names.
        Log?.Invoke(this, $"    MouseDown: ({action.X}, {action.Y})");
        await Task.CompletedTask;
    }

    private async Task ExecuteMouseUpAsync(UIAction action)
    {
        Log?.Invoke(this, $"    MouseUp: ({action.X}, {action.Y})");
        await Task.CompletedTask;
    }

    private async Task ExecuteStartVideoAsync(UIAction action)
    {
        var fps = int.TryParse(action.Value, out var f) ? f : 5;
        var window = GetTargetWindow(action.WindowId);

        _videoRecorder = new GifRecorder(fps, msg => Log?.Invoke(this, $"    {msg}"));
        _videoRecorder.StartRecording(window!);
    }

    private async Task ExecuteStopVideoAsync(UIAction action, UIActionResult result)
    {
        if (_videoRecorder == null) return;

        await _videoRecorder.StopRecordingAsync();
        var name = action.Value ?? $"video_{DateTime.UtcNow:HHmmss_fff}";
        var gifPath = Path.Combine(_screenshotDir, $"{name}.gif");
        await _videoRecorder.SaveAsync(gifPath);

        // Try MP4 export
        var mp4Path = Path.Combine(_screenshotDir, $"{name}.mp4");
        await _videoRecorder.TryExportMp4Async(mp4Path);

        result.ScreenshotPath = gifPath;
        await _videoRecorder.DisposeAsync();
        _videoRecorder = null;
    }

    private async Task ExecuteSvgAsync(UIAction action, UIActionResult result)
    {
        var name = action.Value ?? $"svg_{DateTime.UtcNow:HHmmss_fff}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.svg");
        var window = GetTargetWindow(action.WindowId);
        if (window == null) return;

        var svgContent = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.UpdateLayout();
            var exporter = new Svg.SvgExporter();
            return exporter.Export(window);
        });

        await File.WriteAllTextAsync(filePath, svgContent);
        var sizeKb = new FileInfo(filePath).Length / 1024;
        Log?.Invoke(this, $"    SVG saved: {filePath} ({sizeKb}KB)");
        result.ScreenshotPath = filePath;
    }

    private Window? GetTargetWindow(string? windowId)
    {
        return _context.FindWindow(windowId) ?? _window;
    }

    private Control? FindControl(string? name, Window? window = null)
    {
        var target = window ?? _window;
        if (string.IsNullOrEmpty(name) || target == null) return null;
        if (target.Name == name) return target;
        return target.FindControl<Control>(name);
    }

    private static ScrollViewer? FindFirstScrollViewer(Control? root)
    {
        if (root is null) return null;
        return root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private Task<string> CaptureScreenshotAsync(string name, string? windowId = null)
    {
        var window = GetTargetWindow(windowId);
        if (window == null) return Task.FromResult("");

        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");

        var width = Math.Max(100, (int)window.Bounds.Width);
        var height = Math.Max(100, (int)window.Bounds.Height);

        Log?.Invoke(this, $"    Capturing {width}x{height}");

        return CaptureWindowScreenshotAsync(window, filePath, width, height);
    }

    private async Task<string> CaptureWindowScreenshotAsync(Window window, string filePath, int width, int height)
    {
        var tcs = new TaskCompletionSource<string>();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                window.UpdateLayout();

                var size = new PixelSize(width, height);
                var dpi = new Vector(96, 96);

                using var bitmap = new RenderTargetBitmap(size, dpi);
                bitmap.Render(window);

                using var stream = File.Create(filePath);
                bitmap.Save(stream);

                var fileInfo = new FileInfo(filePath);
                Log?.Invoke(this, $"    Saved {fileInfo.Length / 1024}KB");

                tcs.SetResult(filePath);
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"    Screenshot failed: {ex.Message}");
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Render);

        return await tcs.Task;
    }
}