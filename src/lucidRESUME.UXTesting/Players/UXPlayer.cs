using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using lucidRESUME.UXTesting.Scripts;

namespace lucidRESUME.UXTesting.Players;

public sealed class UXPlayer
{
    private readonly string _screenshotDir;
    private readonly int _defaultDelay;
    private readonly bool _captureScreenshots;
    private Window? _window;
    private Action<string>? _navigateAction;

    public event EventHandler<string>? Log;
    public event EventHandler<UXActionResult>? ActionCompleted;

    public UXPlayer(string screenshotDir, int defaultDelay = 200, bool captureScreenshots = true)
    {
        _screenshotDir = screenshotDir;
        _defaultDelay = defaultDelay;
        _captureScreenshots = captureScreenshots;
        Directory.CreateDirectory(screenshotDir);
    }

    public void SetNavigateAction(Action<string> navigate)
    {
        _navigateAction = navigate;
    }

    public async Task<UXTestResult> RunScriptAsync(Window window, UXScript script)
    {
        _window = window;
        
        var result = new UXTestResult
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

        result.EndTime = DateTime.UtcNow;
        
        Log?.Invoke(this, $"Completed: {result.Duration.TotalSeconds:F2}s");
        
        return result;
    }

    private async Task<UXActionResult> ExecuteActionAsync(UXAction action, int defaultDelay)
    {
        var sw = Stopwatch.StartNew();
        var result = new UXActionResult
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
                case ActionType.TypeText:
                    await ExecuteTypeTextAsync(action);
                    break;
                case ActionType.PressKey:
                    await ExecutePressKeyAsync(action);
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
                case ActionType.Svg:
                    await ExecuteSvgAsync(action, result);
                    break;
                case ActionType.ImportFile:
                    await ExecuteImportFileAsync(action);
                    break;
                case ActionType.PasteJob:
                    await ExecutePasteJobAsync(action);
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
        
        if (_captureScreenshots && action.Type != ActionType.Screenshot)
        {
            result.ScreenshotPath = await CaptureScreenshotAsync($"{action.Type}_{DateTime.UtcNow:HHmmss_fff}");
        }

        return result;
    }

    private async Task ExecuteNavigateAsync(UXAction action)
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

    private async Task ExecuteClickAsync(UXAction action)
    {
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var control = FindControl(action.Target);
                if (control == null)
                {
                    Log?.Invoke(this, $"    Control not found: {action.Target} - skipping");
                    tcs.SetResult();
                    return;
                }

                if (control is Button button && button.Command?.CanExecute(button.CommandParameter) == true)
                {
                    button.Command.Execute(button.CommandParameter);
                    // Give async commands time to start
                    await Task.Delay(100);
                }
                else if (control is TabItem tabItem)
                {
                    tabItem.IsSelected = true;
                }

                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"    Click failed: {ex.Message}");
                tcs.SetResult();
            }
        });
        await tcs.Task;
        await Task.Delay(50);
    }

    private async Task ExecuteDoubleClickAsync(UXAction action)
    {
        var control = FindControl(action.Target);
        if (control == null)
        {
            throw new InvalidOperationException($"Control not found: {action.Target}");
        }

        control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
        await Task.Delay(50);
    }

    private async Task ExecuteTypeTextAsync(UXAction action)
    {
        if (string.IsNullOrEmpty(action.Value)) return;
        
        var control = FindControl(action.Target);
        if (control is TextBox textBox)
        {
            textBox.Text = action.Value;
        }
        
        await Task.Delay(50);
    }

    private async Task ExecutePressKeyAsync(UXAction action)
    {
        if (string.IsNullOrEmpty(action.Value)) return;
        
        var control = FindControl(action.Target) ?? _window;
        var key = Enum.Parse<Key>(action.Value, true);
        
        control?.RaiseEvent(new KeyEventArgs
        {
            Key = key,
            RoutedEvent = InputElement.KeyDownEvent
        });
        
        await Task.Delay(50);
    }

    private async Task ExecuteScrollAsync(UXAction action)
    {
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            // Find named or first ScrollViewer
            ScrollViewer? sv = action.Target != null
                ? _window?.FindControl<ScrollViewer>(action.Target)
                : FindFirstScrollViewer(_window);

            if (sv != null)
            {
                var value = action.Value?.ToLowerInvariant() ?? "down";
                switch (value)
                {
                    case "top":    sv.ScrollToHome(); break;
                    case "bottom": sv.ScrollToEnd(); break;
                    case "up":     sv.LineUp(); break;
                    default:       sv.LineDown(); break;
                }
            }
            tcs.SetResult();
        }, DispatcherPriority.Normal);
        await tcs.Task;
        await Task.Delay(100);
    }

    private static ScrollViewer? FindFirstScrollViewer(Control? root)
    {
        if (root is null) return null;
        return root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private async Task ExecuteWaitAsync(UXAction action)
    {
        var ms = int.TryParse(action.Value, out var v) ? v : action.DelayMs;
        if (ms > 0)
        {
            await Task.Delay(ms);
        }
    }

    private async Task ExecuteScreenshotAsync(UXAction action, UXActionResult result)
    {
        var name = action.Value ?? $"screenshot_{DateTime.UtcNow:HHmmss_fff}";
        result.ScreenshotPath = await CaptureScreenshotAsync(name);
    }

    private async Task ExecuteAssertAsync(UXAction action)
    {
        var control = FindControl(action.Target);
        if (control == null)
        {
            throw new InvalidOperationException($"Assert failed: Control not found: {action.Target}");
        }

        if (action.Value?.StartsWith("visible:") == true)
        {
            var expected = action.Value["visible:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (control.IsVisible != expected)
            {
                throw new InvalidOperationException($"Assert failed: {action.Target}.IsVisible != {expected}");
            }
        }
        else if (action.Value?.StartsWith("enabled:") == true)
        {
            var expected = action.Value["enabled:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (control.IsEnabled != expected)
            {
                throw new InvalidOperationException($"Assert failed: {action.Target}.IsEnabled != {expected}");
            }
        }
        else if (action.Value?.StartsWith("text:") == true && control is TextBox textBox)
        {
            var expected = action.Value["text:".Length..].Trim();
            if (textBox.Text != expected)
            {
                throw new InvalidOperationException($"Assert failed: {action.Target}.Text != {expected}");
            }
        }
        
        await Task.CompletedTask;
    }

    private async Task ExecuteSvgAsync(UXAction action, UXActionResult result)
    {
        if (_window == null) return;
        var name = action.Value ?? $"svg_{DateTime.UtcNow:HHmmss_fff}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.svg");

        var tcs = new TaskCompletionSource<string>();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _window!.UpdateLayout();
                var exporter = new Mostlylucid.Avalonia.UITesting.Svg.SvgExporter();
                var svgContent = exporter.Export(_window);
                File.WriteAllText(filePath, svgContent);
                var fileInfo = new FileInfo(filePath);
                Log?.Invoke(this, $"    SVG {fileInfo.Length / 1024}KB");
                tcs.SetResult(filePath);
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"    SVG failed: {ex.Message}");
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Render);

        result.ScreenshotPath = await tcs.Task;
    }

    private Control? FindControl(string? name)
    {
        if (string.IsNullOrEmpty(name) || _window == null) return null;

        if (_window.Name == name) return _window;

        // Try by x:Name first
        var byName = _window.FindControl<Control>(name);
        if (byName is not null) return byName;

        // Fallback: find Button by Content text (case-insensitive)
        var button = _window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b =>
                b.Content is string s && s.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (button is not null) return button;

        // Fallback: find TextBlock by Text (for labels/tabs)
        var textBlock = _window.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);

        return textBlock?.Parent as Control ?? textBlock;
    }

    private Task<string> CaptureScreenshotAsync(string name)
    {
        if (_window == null) return Task.FromResult("");
        
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        
        var width = Math.Max(100, (int)_window.Bounds.Width);
        var height = Math.Max(100, (int)_window.Bounds.Height);
        
        Log?.Invoke(this, $"    Capturing {width}x{height}");
        
        return CaptureAvaloniaScreenshotAsync(filePath, width, height);
    }
    
    private async Task<string> CaptureAvaloniaScreenshotAsync(string filePath, int width, int height)
    {
        var tcs = new TaskCompletionSource<string>();
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _window!.UpdateLayout();
                
                var size = new PixelSize(width, height);
                var dpi = new Vector(96, 96);
                
                using var bitmap = new RenderTargetBitmap(size, dpi);
                bitmap.Render(_window);
                
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

    /// <summary>
    /// ImportFile action: programmatically imports a file (bypasses file picker dialog).
    /// Value = file path to import. Works with resume pages.
    /// </summary>
    private async Task ExecuteImportFileAsync(UXAction action)
    {
        if (_window == null || string.IsNullOrEmpty(action.Value)) return;

        var filePath = Path.IsPathRooted(action.Value)
            ? action.Value
            : Path.Combine(Directory.GetCurrentDirectory(), action.Value);

        if (!File.Exists(filePath))
        {
            Log?.Invoke(this, $"    File not found: {filePath}");
            return;
        }

        // Resolve the ResumePageViewModel and call import directly
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                _navigateAction?.Invoke("resume");
                await Task.Delay(200);

                var vm = _window.DataContext;
                var getPage = vm?.GetType().GetMethod("GetPage");
                var resumeVm = getPage?.Invoke(vm, ["Resume"]);
                if (resumeVm != null)
                {
                    var method = resumeVm.GetType().GetMethod("ImportFromPathAsync");
                    if (method != null)
                    {
                        await (Task)method.Invoke(resumeVm, [filePath])!;
                        Log?.Invoke(this, $"    Imported: {Path.GetFileName(filePath)}");
                    }
                }
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"    Import failed: {ex.Message}");
                tcs.SetResult();
            }
        });
        await tcs.Task;
    }

    /// <summary>
    /// PasteJob action: programmatically adds a job from text (bypasses UI interaction).
    /// Value = job description text or @filepath to read from file.
    /// </summary>
    private async Task ExecutePasteJobAsync(UXAction action)
    {
        if (_window == null || string.IsNullOrEmpty(action.Value)) return;

        var jobText = action.Value.StartsWith('@')
            ? await File.ReadAllTextAsync(action.Value[1..])
            : action.Value;

        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var vm = _window.DataContext;
                _navigateAction?.Invoke("search");
                await Task.Delay(300);

                var getPage = vm?.GetType().GetMethod("GetPage");
                var searchVm = getPage?.Invoke(vm, ["Search"]);
                if (searchVm != null)
                {
                    var method = searchVm.GetType().GetMethod("AddJobFromTextAsync");
                    if (method != null)
                    {
                        await (Task)method.Invoke(searchVm, [jobText])!;
                        Log?.Invoke(this, $"    Pasted job: {jobText[..Math.Min(60, jobText.Length)]}...");
                    }
                }
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"    PasteJob failed: {ex.Message}");
                tcs.SetResult();
            }
        });
        await tcs.Task;
    }
}