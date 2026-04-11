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
using Mostlylucid.Avalonia.UITesting.Expect;
using Mostlylucid.Avalonia.UITesting.Locators;
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
    private readonly PointerSimulator _pointer = new();
    private readonly LocatorEngine _locators = new();
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
                case ActionType.Expect:
                    await ExecuteExpectAsync(action);
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
                case ActionType.Drag:
                    await ExecuteDragAsync(action);
                    break;
                case ActionType.Wheel:
                    await ExecuteWheelAsync(action);
                    break;
                case ActionType.Pinch:
                    await ExecutePinchAsync(action);
                    break;
                case ActionType.Rotate:
                    await ExecuteRotateAsync(action);
                    break;
                case ActionType.Swipe:
                    await ExecuteSwipeAsync(action);
                    break;
                case ActionType.TouchTap:
                    await ExecuteTouchTapAsync(action);
                    break;
                case ActionType.TouchDown:
                    await ExecuteTouchDownAsync(action);
                    break;
                case ActionType.TouchMove:
                    await ExecuteTouchMoveAsync(action);
                    break;
                case ActionType.TouchUp:
                    await ExecuteTouchUpAsync(action);
                    break;
                case ActionType.TouchDrag:
                    await ExecuteTouchDragAsync(action);
                    break;
                case ActionType.WindowResize:
                    await ExecuteWindowResizeAsync(action);
                    break;
                case ActionType.WindowMove:
                    await ExecuteWindowMoveAsync(action);
                    break;
                case ActionType.WindowMinimize:
                    await ExecuteWindowStateAsync(action, WindowState.Minimized);
                    break;
                case ActionType.WindowMaximize:
                    await ExecuteWindowStateAsync(action, WindowState.Maximized);
                    break;
                case ActionType.WindowRestore:
                    await ExecuteWindowStateAsync(action, WindowState.Normal);
                    break;
                case ActionType.WindowClose:
                    await ExecuteWindowCloseAsync(action);
                    break;
                case ActionType.WindowFocus:
                    await ExecuteWindowFocusAsync(action);
                    break;
                case ActionType.WindowSetTitle:
                    await ExecuteWindowSetTitleAsync(action);
                    break;
                case ActionType.WindowSetFullScreen:
                    await ExecuteWindowStateAsync(action, WindowState.FullScreen);
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
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Click");
        var control = await LocateAsync(action.Target, window);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (control is Button button)
            {
                // Always raise ClickEvent so the Button's own Click handler (XAML Click="...")
                // runs. Then ALSO invoke any bound Command. This matches what a real pointer
                // press+release would do — both paths fire on a real click.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (button.Command?.CanExecute(button.CommandParameter) == true)
                    button.Command.Execute(button.CommandParameter);
            }
            else if (control is ToggleButton toggle)
            {
                toggle.IsChecked = !(toggle.IsChecked ?? false);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (control is TabItem tabItem)
            {
                tabItem.IsSelected = true;
            }
            else if (control is RadioButton radio)
            {
                radio.IsChecked = true;
                radio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for DoubleClick");
        var control = await LocateAsync(action.Target, window);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
        });
        await Task.Delay(50);
    }

    private async Task ExecuteRightClickAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for RightClick");

        // Coordinate-based right click takes precedence when X/Y are supplied.
        if (action.X.HasValue && action.Y.HasValue)
        {
            await _pointer.ClickAsync(window, action.X.Value, action.Y.Value, MouseButton.Right);
            await Task.Delay(50);
            return;
        }

        var control = await LocateAsync(action.Target, window);

        var (cx, cy) = await GetControlCenterAsync(control, window);
        await _pointer.ClickAsync(window, cx, cy, MouseButton.Right);
        await Task.Delay(50);
    }

    private async Task ExecuteTypeTextAsync(UIAction action)
    {
        if (string.IsNullOrEmpty(action.Value)) return;

        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TypeText");
        var control = await LocateAsync(action.Target, window);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (control is TextBox textBox)
            {
                textBox.Text = action.Value;
            }
            else
            {
                throw new InvalidOperationException(
                    $"TypeText target '{action.Target}' resolved to {control.GetType().Name}, expected TextBox");
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
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Hover");

        double x, y;
        if (action.X.HasValue && action.Y.HasValue)
        {
            (x, y) = (action.X.Value, action.Y.Value);
        }
        else
        {
            var control = await LocateAsync(action.Target, window);
            (x, y) = await GetControlCenterAsync(control, window);
        }

        var linger = action.DelayMs > 0 ? action.DelayMs : 250;
        await _pointer.HoverAsync(window, x, y, linger);
        Log?.Invoke(this, $"    Hover: ({x:F0}, {y:F0}) linger={linger}ms");
    }

    private async Task ExecuteScrollAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId);
        if (window == null) return;

        // Resolve the target via the locator engine so namescope-isolated
        // ScrollViewers (inside UserControls, popups, templated parts) are found.
        ScrollViewer? sv = null;
        if (!string.IsNullOrEmpty(action.Target))
        {
            var control = await LocateAsync(action.Target, window);
            sv = control as ScrollViewer ?? control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }
        else
        {
            sv = FindFirstScrollViewer(window);
        }

        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {

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
        var window = GetTargetWindow(action.WindowId);
        if (window == null)
        {
            result.ScreenshotPath = "";
            return;
        }

        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var padding = action.Padding ?? 0;

        // Snip a control by selector → its bounds (+ optional padding)
        if (!string.IsNullOrEmpty(action.Target))
        {
            var control = await LocateAsync(action.Target, window);
            result.ScreenshotPath = await ScreenshotCapture.CaptureControlAsync(window, control, filePath, padding);
            Log?.Invoke(this, $"    Snip control {action.Target} → {Path.GetFileName(filePath)}");
            return;
        }

        // Snip an explicit rect via X/Y/X2/Y2
        if (action.X.HasValue && action.Y.HasValue && action.X2.HasValue && action.Y2.HasValue)
        {
            var rect = new Rect(action.X.Value, action.Y.Value,
                action.X2.Value - action.X.Value, action.Y2.Value - action.Y.Value);
            if (padding > 0) rect = rect.Inflate(padding);
            result.ScreenshotPath = await ScreenshotCapture.CaptureRegionAsync(window, filePath, rect);
            Log?.Invoke(this, $"    Snip region {rect} → {Path.GetFileName(filePath)}");
            return;
        }

        // Otherwise full window
        result.ScreenshotPath = await ScreenshotCapture.CaptureWindowAsync(window, filePath);
    }

    private async Task ExecuteAssertAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Assert");
        var control = await LocateAsync(action.Target, window);

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

    private async Task ExecuteExpectAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Expect");
        if (string.IsNullOrEmpty(action.Target))
            throw new InvalidOperationException("Expect requires a target locator");
        if (string.IsNullOrEmpty(action.Matcher))
            throw new InvalidOperationException("Expect requires a matcher (HasText, IsVisible, IsEnabled, ...)");

        var locator = SelectorParser.Parse(action.Target);
        var matcher = BuildMatcher(action.Matcher, action.Value);

        var expectation = new Expectation(locator, matcher);
        if (action.Timeout is int t) expectation = new Expectation(locator, matcher) { TimeoutMs = t };

        await expectation.AssertAsync(window, _locators);
        Log?.Invoke(this, $"    Expect {action.Target} to {matcher.Describe()}: ok");
    }

    private static Matcher BuildMatcher(string name, string? value)
    {
        var key = name.Trim();
        // Allow "Not.HasText" / "not.HasText" / "!HasText" prefixes for negation.
        var negate = false;
        if (key.StartsWith("!"))
        {
            negate = true;
            key = key[1..];
        }
        else if (key.StartsWith("Not.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("not."))
        {
            negate = true;
            key = key[4..];
        }

        Matcher matcher = key.ToLowerInvariant() switch
        {
            "isvisible" or "visible" => new IsVisibleMatcher(),
            "ishidden" or "hidden" => new IsHiddenMatcher(),
            "isenabled" or "enabled" => new IsEnabledMatcher(),
            "isdisabled" or "disabled" => new IsDisabledMatcher(),
            "ischecked" or "checked" => new IsCheckedMatcher(),
            "isunchecked" or "unchecked" => new IsUncheckedMatcher(),
            "isfocused" or "focused" => new IsFocusedMatcher(),
            "hastext" => new HasTextMatcher(value ?? throw new InvalidOperationException("HasText requires value"), exact: true),
            "containstext" or "contains" => new ContainsTextMatcher(value ?? throw new InvalidOperationException("ContainsText requires value")),
            "matchesregex" or "regex" => new MatchesRegexMatcher(value ?? throw new InvalidOperationException("MatchesRegex requires value")),
            "hascount" or "count" => new HasCountMatcher(int.Parse(value ?? throw new InvalidOperationException("HasCount requires value"))),
            "hasvalue" or "value" => new HasValueMatcher(value ?? throw new InvalidOperationException("HasValue requires value")),
            "hasproperty" or "property" => BuildHasProperty(value),
            _ => throw new InvalidOperationException($"Unknown matcher '{name}'")
        };

        return negate ? matcher.Not() : matcher;
    }

    private static HasPropertyMatcher BuildHasProperty(string? value)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException("HasProperty requires value in 'PropertyName=expected' form");
        var eq = value.IndexOf('=');
        if (eq <= 0)
            throw new InvalidOperationException("HasProperty value must be 'PropertyName=expected'");
        return new HasPropertyMatcher(value[..eq], value[(eq + 1)..]);
    }

    private async Task ExecuteMouseMoveAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for MouseMove");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        await _pointer.MoveAsync(window, x, y);
        Log?.Invoke(this, $"    MouseMove: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteMouseDownAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for MouseDown");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        var button = ParseButton(action.Button);
        await _pointer.DownAsync(window, x, y, button);
        Log?.Invoke(this, $"    MouseDown[{button}]: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteMouseUpAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for MouseUp");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        var button = ParseButton(action.Button);
        await _pointer.UpAsync(window, x, y, button);
        Log?.Invoke(this, $"    MouseUp[{button}]: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteDragAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Drag");

        if (!action.X.HasValue || !action.Y.HasValue || !action.X2.HasValue || !action.Y2.HasValue)
            throw new InvalidOperationException("Drag requires X, Y, X2, Y2");

        var button = ParseButton(action.Button);
        var steps = action.Steps ?? 10;
        await _pointer.DragAsync(window, action.X.Value, action.Y.Value, action.X2.Value, action.Y2.Value, steps, 16, button);
        Log?.Invoke(this, $"    Drag[{button}]: ({action.X:F0},{action.Y:F0}) → ({action.X2:F0},{action.Y2:F0}) steps={steps}");
    }

    private async Task ExecuteWheelAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Wheel");
        var (x, y) = await ResolveCoordinatesAsync(action, window);

        // Value carries delta. Format: "dx,dy" or "dy" alone (positive = up).
        double dx = 0, dy = -1;
        if (!string.IsNullOrEmpty(action.Value))
        {
            var parts = action.Value.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 1 && double.TryParse(parts[0], out var single))
            {
                dy = single;
            }
            else if (parts.Length == 2
                     && double.TryParse(parts[0], out var px)
                     && double.TryParse(parts[1], out var py))
            {
                dx = px; dy = py;
            }
        }

        await _pointer.WheelAsync(window, x, y, dx, dy);
        Log?.Invoke(this, $"    Wheel: ({x:F0},{y:F0}) Δ=({dx},{dy})");
    }

    private async Task ExecutePinchAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Pinch");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        var scaleDelta = double.TryParse(action.Value, out var s) ? s : 0.1;
        var steps = action.Steps ?? 10;
        for (int i = 0; i < steps; i++)
        {
            await _pointer.MagnifyAsync(window, x, y, scaleDelta / steps);
            await Task.Delay(16);
        }
        Log?.Invoke(this, $"    Pinch: ({x:F0},{y:F0}) total scale Δ={scaleDelta}");
    }

    private async Task ExecuteRotateAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Rotate");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        var angle = double.TryParse(action.Value, out var a) ? a : 30.0;
        var steps = action.Steps ?? 10;
        for (int i = 0; i < steps; i++)
        {
            await _pointer.RotateAsync(window, x, y, angle / steps);
            await Task.Delay(16);
        }
        Log?.Invoke(this, $"    Rotate: ({x:F0},{y:F0}) total Δ={angle}°");
    }

    private async Task ExecuteSwipeAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for Swipe");
        var (x, y) = await ResolveCoordinatesAsync(action, window);

        double dx = 0, dy = 0;
        if (action.X2.HasValue && action.Y2.HasValue)
        {
            dx = action.X2.Value - x;
            dy = action.Y2.Value - y;
        }
        else if (!string.IsNullOrEmpty(action.Value))
        {
            var parts = action.Value.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out var px) && double.TryParse(parts[1], out var py))
            {
                dx = px; dy = py;
            }
        }

        var steps = action.Steps ?? 5;
        for (int i = 0; i < steps; i++)
        {
            await _pointer.SwipeAsync(window, x, y, dx / steps, dy / steps);
            await Task.Delay(16);
        }
        Log?.Invoke(this, $"    Swipe: ({x:F0},{y:F0}) Δ=({dx},{dy})");
    }

    private async Task ExecuteTouchTapAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TouchTap");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        await _pointer.TouchTapAsync(window, x, y);
        Log?.Invoke(this, $"    TouchTap: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteTouchDownAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TouchDown");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        await _pointer.TouchDownAsync(window, x, y);
        Log?.Invoke(this, $"    TouchDown: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteTouchMoveAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TouchMove");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        await _pointer.TouchMoveAsync(window, x, y);
        Log?.Invoke(this, $"    TouchMove: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteTouchUpAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TouchUp");
        var (x, y) = await ResolveCoordinatesAsync(action, window);
        await _pointer.TouchUpAsync(window, x, y);
        Log?.Invoke(this, $"    TouchUp: ({x:F0}, {y:F0})");
    }

    private async Task ExecuteTouchDragAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for TouchDrag");
        if (!action.X.HasValue || !action.Y.HasValue || !action.X2.HasValue || !action.Y2.HasValue)
            throw new InvalidOperationException("TouchDrag requires X, Y, X2, Y2");
        var steps = action.Steps ?? 10;
        await _pointer.TouchDragAsync(window, action.X.Value, action.Y.Value, action.X2.Value, action.Y2.Value, steps);
        Log?.Invoke(this, $"    TouchDrag: ({action.X:F0},{action.Y:F0}) → ({action.X2:F0},{action.Y2:F0})");
    }

    private async Task ExecuteWindowResizeAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for WindowResize");
        if (!action.X.HasValue || !action.Y.HasValue)
            throw new InvalidOperationException("WindowResize requires X (width) and Y (height)");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.Width = action.X.Value;
            window.Height = action.Y.Value;
        });
        Log?.Invoke(this, $"    WindowResize: {action.X}x{action.Y}");
    }

    private async Task ExecuteWindowMoveAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for WindowMove");
        if (!action.X.HasValue || !action.Y.HasValue)
            throw new InvalidOperationException("WindowMove requires X and Y screen coordinates");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.Position = new PixelPoint((int)action.X.Value, (int)action.Y.Value);
        });
        Log?.Invoke(this, $"    WindowMove: ({action.X},{action.Y})");
    }

    private async Task ExecuteWindowStateAsync(UIAction action, WindowState state)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException($"No target window for {state}");
        await Dispatcher.UIThread.InvokeAsync(() => window.WindowState = state);
        Log?.Invoke(this, $"    WindowState: {state}");
    }

    private async Task ExecuteWindowCloseAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for WindowClose");
        await Dispatcher.UIThread.InvokeAsync(() => window.Close());
        Log?.Invoke(this, "    WindowClose");
    }

    private async Task ExecuteWindowFocusAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for WindowFocus");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.Activate();
            window.Focus();
        });
        Log?.Invoke(this, "    WindowFocus");
    }

    private async Task ExecuteWindowSetTitleAsync(UIAction action)
    {
        var window = GetTargetWindow(action.WindowId)
            ?? throw new InvalidOperationException("No target window for WindowSetTitle");
        var title = action.Value ?? "";
        await Dispatcher.UIThread.InvokeAsync(() => window.Title = title);
        Log?.Invoke(this, $"    WindowSetTitle: {title}");
    }

    private async Task<(double X, double Y)> ResolveCoordinatesAsync(UIAction action, Window window)
    {
        if (action.X.HasValue && action.Y.HasValue)
            return (action.X.Value, action.Y.Value);

        if (!string.IsNullOrEmpty(action.Target))
        {
            var control = await LocateAsync(action.Target, window);
            return await GetControlCenterAsync(control, window);
        }

        throw new InvalidOperationException($"{action.Type} requires either X/Y or a Target control selector");
    }

    private static async Task<(double X, double Y)> GetControlCenterAsync(Control control, Window window)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var topLeft = control.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
            var bounds = control.Bounds;
            return (topLeft.X + bounds.Width / 2, topLeft.Y + bounds.Height / 2);
        });
    }

    private static MouseButton ParseButton(string? button)
    {
        if (string.IsNullOrEmpty(button)) return MouseButton.Left;
        return button.ToLowerInvariant() switch
        {
            "right" or "rmb" or "secondary" => MouseButton.Right,
            "middle" or "wheel" or "mmb" => MouseButton.Middle,
            "x1" or "xbutton1" or "back" => MouseButton.XButton1,
            "x2" or "xbutton2" or "forward" => MouseButton.XButton2,
            _ => MouseButton.Left
        };
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

    /// <summary>
    /// Resolve a target string to a control via the locator engine, with auto-retry
    /// up to <paramref name="timeoutMs"/>. The string is parsed as a selector — bare
    /// words are treated as <c>name=&lt;word&gt;</c> for backwards compatibility.
    /// </summary>
    private async Task<Control> LocateAsync(string? target, Window window, int timeoutMs = 5000)
    {
        if (string.IsNullOrEmpty(target))
            throw new InvalidOperationException("Locator target is null or empty");
        var locator = SelectorParser.Parse(target);
        return await _locators.ResolveFirstAsync(locator, window, timeoutMs);
    }

    /// <summary>
    /// Synchronous, non-retrying control lookup retained for the few call sites that
    /// only need a one-shot best-effort find (e.g. press-key falling back to the
    /// window itself when the target is missing).
    /// </summary>
    private Control? FindControl(string? name, Window? window = null)
    {
        var target = window ?? _window;
        if (string.IsNullOrEmpty(name) || target == null) return null;
        try
        {
            var locator = SelectorParser.Parse(name);
            return locator.Resolve(target).FirstOrDefault();
        }
        catch (SelectorParseException)
        {
            return null;
        }
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