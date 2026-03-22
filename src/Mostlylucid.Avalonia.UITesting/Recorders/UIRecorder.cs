using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Mostlylucid.Avalonia.UITesting.Scripts;
using Mostlylucid.Avalonia.UITesting.Video;

namespace Mostlylucid.Avalonia.UITesting.Recorders;

public sealed class UIRecorder : IAsyncDisposable
{
    private readonly List<UIAction> _actions = new();
    private readonly List<Window> _attachedWindows = new();
    private readonly object _lock = new();
    private bool _isRecording;
    private bool _isPaused;
    private DateTime _lastActionTime;
    private Action<string>? _navigateCallback;
    private GifRecorder? _videoRecorder;
    private readonly bool _capturePositions;
    private readonly bool _crossWindowTracking;
    private readonly int _coalesceThresholdMs;

    public event EventHandler<string>? Log;
    public event EventHandler<UIAction>? ActionRecorded;

    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public IReadOnlyList<UIAction> Actions { get { lock (_lock) return _actions.ToList().AsReadOnly(); } }

    public UIRecorder(UIRecorderOptions? options = null)
    {
        var opts = options ?? new UIRecorderOptions();
        _capturePositions = opts.CaptureMousePositions;
        _crossWindowTracking = opts.CrossWindowTracking;
        _coalesceThresholdMs = opts.CoalesceThresholdMs;
    }

    public void StartRecording(Window window, bool recordVideo = false, int videoFps = 5)
    {
        if (_isRecording) return;

        _isRecording = true;
        _isPaused = false;
        _lastActionTime = DateTime.UtcNow;
        lock (_lock) _actions.Clear();

        AttachWindow(window);

        if (_crossWindowTracking)
        {
            EnableCrossWindowTracking();
        }

        if (recordVideo)
        {
            _videoRecorder = new GifRecorder(videoFps, msg => Log?.Invoke(this, msg));
            _videoRecorder.StartRecording(window);
        }

        Log?.Invoke(this, "Recording started");
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        foreach (var window in _attachedWindows.ToList())
            DetachWindow(window);

        _isRecording = false;
        _isPaused = false;

        if (_videoRecorder != null)
        {
            await _videoRecorder.StopRecordingAsync();
        }

        Log?.Invoke(this, $"Recording stopped. {_actions.Count} actions captured");
    }

    public void PauseRecording()
    {
        if (!_isRecording || _isPaused) return;
        _isPaused = true;
        Log?.Invoke(this, "Recording paused");
    }

    public void ResumeRecording()
    {
        if (!_isRecording || !_isPaused) return;
        _isPaused = false;
        _lastActionTime = DateTime.UtcNow;
        Log?.Invoke(this, "Recording resumed");
    }

    public void OnNavigated(string page)
    {
        if (!_isRecording || _isPaused) return;

        var action = new UIAction
        {
            Type = ActionType.Navigate,
            Value = page,
            Description = $"Navigate to {page}",
            DelayMs = CalculateDelay()
        };

        RecordAction(action);
    }

    public void SetNavigateCallback(Action<string> callback)
    {
        _navigateCallback = callback;
    }

    public UIScript GenerateScript(string name, string? description = null)
    {
        List<UIAction> actions;
        lock (_lock) actions = CoalesceActions(_actions);

        return new UIScript
        {
            Name = name,
            Description = description ?? $"Recorded on {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Actions = actions
        };
    }

    public void SaveAsYaml(string path, string? name = null)
    {
        var script = GenerateScript(name ?? Path.GetFileNameWithoutExtension(path));
        ScriptLoader.SaveAsYaml(script, path);
        Log?.Invoke(this, $"Script saved: {path}");
    }

    public void SaveAsJson(string path, string? name = null)
    {
        var script = GenerateScript(name ?? Path.GetFileNameWithoutExtension(path));
        ScriptLoader.SaveAsJson(script, path);
        Log?.Invoke(this, $"Script saved: {path}");
    }

    public async Task<string?> SaveVideoAsync(string filePath)
    {
        if (_videoRecorder == null) return null;
        return await _videoRecorder.SaveAsync(filePath);
    }

    public async Task<string?> TryExportVideoMp4Async(string filePath)
    {
        if (_videoRecorder == null) return null;
        return await _videoRecorder.TryExportMp4Async(filePath);
    }

    private void AttachWindow(Window window)
    {
        if (_attachedWindows.Contains(window)) return;

        _attachedWindows.Add(window);

        window.PointerPressed += OnPointerPressed;
        window.PointerReleased += OnPointerReleased;
        window.PointerMoved += OnPointerMoved;
        window.PointerWheelChanged += OnPointerWheelChanged;
        window.KeyDown += OnKeyDown;
        window.AddHandler(TextBox.TextChangedEvent, OnTextChanged);

        var windowId = GetWindowIdentifier(window);
        Log?.Invoke(this, $"Attached to window: {windowId}");
    }

    private void DetachWindow(Window window)
    {
        if (!_attachedWindows.Contains(window)) return;

        window.PointerPressed -= OnPointerPressed;
        window.PointerReleased -= OnPointerReleased;
        window.PointerMoved -= OnPointerMoved;
        window.PointerWheelChanged -= OnPointerWheelChanged;
        window.KeyDown -= OnKeyDown;
        window.RemoveHandler(TextBox.TextChangedEvent, OnTextChanged);

        _attachedWindows.Remove(window);
    }

    private void EnableCrossWindowTracking()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (_isRecording && !_attachedWindows.Contains(window))
            {
                AttachWindow(window);
            }
        });

        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) =>
        {
            DetachWindow(window);
        });
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isRecording || _isPaused) return;

        var source = e.Source as Control;
        var target = FindClickableParent(source);
        var window = sender as Window;

        if (target == null && !_capturePositions) return;

        if (_capturePositions && target == null)
        {
            // Record raw mouse position
            var pos = e.GetPosition(window);
            var action = new UIAction
            {
                Type = ActionType.MouseDown,
                X = pos.X,
                Y = pos.Y,
                WindowId = GetWindowIdentifier(window),
                Description = $"Mouse down at ({pos.X:F0}, {pos.Y:F0})",
                DelayMs = CalculateDelay()
            };
            RecordAction(action);
            return;
        }

        if (target != null)
        {
            var actionType = e.ClickCount > 1 ? ActionType.DoubleClick : ActionType.Click;
            var controlId = GetControlIdentifier(target);

            var action = new UIAction
            {
                Type = actionType,
                Target = controlId,
                WindowId = _crossWindowTracking ? GetWindowIdentifier(window) : null,
                Description = $"{actionType} on {target.GetType().Name}",
                DelayMs = CalculateDelay()
            };

            if (_capturePositions)
            {
                var pos = e.GetPosition(window);
                action.X = pos.X;
                action.Y = pos.Y;
            }

            RecordAction(action);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isRecording || _isPaused || !_capturePositions) return;

        var window = sender as Window;
        var pos = e.GetPosition(window);

        var action = new UIAction
        {
            Type = ActionType.MouseUp,
            X = pos.X,
            Y = pos.Y,
            WindowId = GetWindowIdentifier(window),
            Description = $"Mouse up at ({pos.X:F0}, {pos.Y:F0})",
            DelayMs = CalculateDelay()
        };

        RecordAction(action);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isRecording || _isPaused || !_capturePositions) return;

        var window = sender as Window;
        var pos = e.GetPosition(window);

        // Throttle: only record if moved significantly
        lock (_lock)
        {
            var lastMove = _actions.LastOrDefault(a => a.Type == ActionType.MouseMove);
            if (lastMove != null)
            {
                var dx = Math.Abs((lastMove.X ?? 0) - pos.X);
                var dy = Math.Abs((lastMove.Y ?? 0) - pos.Y);
                if (dx < 10 && dy < 10) return; // Skip small movements
            }
        }

        var action = new UIAction
        {
            Type = ActionType.MouseMove,
            X = pos.X,
            Y = pos.Y,
            WindowId = GetWindowIdentifier(window),
            Description = $"Mouse move to ({pos.X:F0}, {pos.Y:F0})",
            DelayMs = CalculateDelay()
        };

        RecordAction(action);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_isRecording || _isPaused) return;

        var window = sender as Window;

        string direction;
        if (e.Delta.Y > 0) direction = "up";
        else if (e.Delta.Y < 0) direction = "down";
        else if (e.Delta.X > 0) direction = "right";
        else direction = "left";

        // Find the ScrollViewer under the pointer
        var source = e.Source as Control;
        string? scrollViewerName = null;
        var current = source;
        while (current != null)
        {
            if (current is ScrollViewer sv && !string.IsNullOrEmpty(sv.Name))
            {
                scrollViewerName = sv.Name;
                break;
            }
            current = current.Parent as Control;
        }

        var action = new UIAction
        {
            Type = ActionType.Scroll,
            Target = scrollViewerName,
            Value = direction,
            WindowId = _crossWindowTracking ? GetWindowIdentifier(window) : null,
            Description = $"Scroll {direction}",
            DelayMs = CalculateDelay()
        };

        RecordAction(action);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isRecording || _isPaused) return;

        var window = sender as Window;

        // Only record special keys, not character input (that's handled by text change)
        if (e.Key is Key.Enter or Key.Escape or Key.Tab or Key.Delete or Key.Back
            or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5
            or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F10
            or Key.F11 or Key.F12)
        {
            var action = new UIAction
            {
                Type = ActionType.PressKey,
                Value = e.Key.ToString(),
                Target = GetControlIdentifier(e.Source as Control),
                WindowId = _crossWindowTracking ? GetWindowIdentifier(window) : null,
                Description = $"Press {e.Key}",
                DelayMs = CalculateDelay()
            };

            RecordAction(action);
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isRecording || _isPaused) return;

        var source = e.Source as TextBox;
        if (source == null) return;

        var controlId = GetControlIdentifier(source);

        lock (_lock)
        {
            // Coalesce: update existing text action for the same control
            var lastTextAction = _actions.LastOrDefault(a =>
                a.Type == ActionType.TypeText && a.Target == controlId);

            if (lastTextAction != null)
            {
                lastTextAction.Value = source.Text;
            }
            else
            {
                var window = FindParentWindow(source);
                var action = new UIAction
                {
                    Type = ActionType.TypeText,
                    Target = controlId,
                    Value = source.Text,
                    WindowId = _crossWindowTracking ? GetWindowIdentifier(window) : null,
                    Description = $"Type in {source.Name ?? source.GetType().Name}",
                    DelayMs = CalculateDelay()
                };

                RecordAction(action);
            }
        }
    }

    private void RecordAction(UIAction action)
    {
        lock (_lock) _actions.Add(action);
        _lastActionTime = DateTime.UtcNow;

        Log?.Invoke(this, $"  [{action.Type}] {action.Target ?? ""} {action.Value ?? ""}");
        ActionRecorded?.Invoke(this, action);
    }

    private int CalculateDelay()
    {
        return (int)(DateTime.UtcNow - _lastActionTime).TotalMilliseconds;
    }

    private List<UIAction> CoalesceActions(List<UIAction> actions)
    {
        var result = new List<UIAction>();

        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];

            // Skip mouse moves that are too close together
            if (action.Type == ActionType.MouseMove && i + 1 < actions.Count
                && actions[i + 1].Type == ActionType.MouseMove
                && actions[i + 1].DelayMs < _coalesceThresholdMs)
            {
                continue;
            }

            // Merge consecutive scrolls in the same direction
            if (action.Type == ActionType.Scroll && result.Count > 0
                && result[^1].Type == ActionType.Scroll
                && result[^1].Value == action.Value
                && result[^1].Target == action.Target
                && action.DelayMs < _coalesceThresholdMs)
            {
                continue; // Skip duplicate scroll
            }

            result.Add(action);
        }

        return result;
    }

    private static Control? FindClickableParent(Control? control)
    {
        while (control != null)
        {
            if (control is Button or TabItem or ListBoxItem or MenuItem
                or CheckBox or RadioButton or ComboBox or ToggleSwitch
                or ToggleButton or Slider or NumericUpDown or DatePicker
                or TreeViewItem or ComboBoxItem)
                return control;

            control = control.Parent as Control;
        }
        return null;
    }

    private static string GetControlIdentifier(Control? control)
    {
        if (control == null) return "";

        if (!string.IsNullOrEmpty(control.Name))
            return control.Name;

        var type = control.GetType().Name;

        if (control is Button btn && btn.Content is string text)
            return $"{type}:{text}";

        if (control is TabItem tab && tab.Header is string header)
            return $"{type}:{header}";

        return type;
    }

    private static string? GetWindowIdentifier(Window? window)
    {
        if (window == null) return null;
        if (!string.IsNullOrEmpty(window.Name)) return window.Name;
        if (!string.IsNullOrEmpty(window.Title)) return window.Title;
        return window.GetType().Name;
    }

    private static Window? FindParentWindow(Control? control)
    {
        while (control != null)
        {
            if (control is Window w) return w;
            control = control.Parent as Control;
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRecording)
            await StopRecordingAsync();

        if (_videoRecorder != null)
            await _videoRecorder.DisposeAsync();
    }
}

public class UIRecorderOptions
{
    public bool CaptureMousePositions { get; set; }
    public bool CrossWindowTracking { get; set; } = true;
    public int CoalesceThresholdMs { get; set; } = 50;
}
