using Avalonia.Controls;
using Avalonia.Input;
using lucidRESUME.UXTesting.Scripts;

namespace lucidRESUME.UXTesting.Recorders;

public sealed class UXRecorder
{
    private readonly List<UXAction> _actions = new();
    private Window? _window;
    private bool _isRecording;
    private DateTime _lastActionTime;

    public event EventHandler<string>? Log;
    public event EventHandler<UXAction>? ActionRecorded;

    public bool IsRecording => _isRecording;
    public IReadOnlyList<UXAction> Actions => _actions.AsReadOnly();

    public void StartRecording(Window window)
    {
        if (_isRecording) return;
        
        _window = window;
        _isRecording = true;
        _lastActionTime = DateTime.UtcNow;
        _actions.Clear();
        
        AttachEventHandlers();
        Log?.Invoke(this, "Recording started");
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        
        DetachEventHandlers();
        _isRecording = false;
        Log?.Invoke(this, $"Recording stopped. {_actions.Count} actions captured");
    }

    public UXScript GenerateScript(string name, string? description = null)
    {
        return new UXScript
        {
            Name = name,
            Description = description ?? $"Recorded on {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Actions = new List<UXAction>(_actions)
        };
    }

    private void AttachEventHandlers()
    {
        if (_window == null) return;
        
        _window.PointerPressed += OnPointerPressed;
        _window.KeyDown += OnKeyDown;
        _window.AddHandler(TextBox.TextChangedEvent, OnTextChanged);
    }

    private void DetachEventHandlers()
    {
        if (_window == null) return;
        
        _window.PointerPressed -= OnPointerPressed;
        _window.KeyDown -= OnKeyDown;
        _window.RemoveHandler(TextBox.TextChangedEvent, OnTextChanged);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isRecording) return;
        
        var source = e.Source as Control;
        var target = FindClickableParent(source);
        
        if (target == null) return;
        
        var action = new UXAction
        {
            Type = e.ClickCount > 1 ? ActionType.DoubleClick : ActionType.Click,
            Target = GetControlIdentifier(target),
            Description = $"Click on {target.GetType().Name}",
            DelayMs = CalculateDelay()
        };
        
        RecordAction(action);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isRecording) return;
        
        if (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab)
        {
            var action = new UXAction
            {
                Type = ActionType.PressKey,
                Value = e.Key.ToString(),
                Target = GetControlIdentifier(e.Source as Control),
                Description = $"Press {e.Key}",
                DelayMs = CalculateDelay()
            };
            
            RecordAction(action);
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isRecording) return;
        
        var source = e.Source as TextBox;
        if (source == null) return;
        
        var controlId = GetControlIdentifier(source);
        var lastTextAction = _actions.LastOrDefault(a => 
            a.Type == ActionType.TypeText && a.Target == controlId);
        
        if (lastTextAction != null)
        {
            lastTextAction.Value = source.Text;
        }
        else
        {
            var action = new UXAction
            {
                Type = ActionType.TypeText,
                Target = controlId,
                Value = source.Text,
                Description = $"Type in {source.Name ?? source.GetType().Name}",
                DelayMs = CalculateDelay()
            };
            
            RecordAction(action);
        }
    }

    private void RecordAction(UXAction action)
    {
        _actions.Add(action);
        _lastActionTime = DateTime.UtcNow;
        
        Log?.Invoke(this, $"  [{action.Type}] {action.Target ?? ""} {action.Value ?? ""}");
        ActionRecorded?.Invoke(this, action);
    }

    private int CalculateDelay()
    {
        return (int)(DateTime.UtcNow - _lastActionTime).TotalMilliseconds;
    }

    private static Control? FindClickableParent(Control? control)
    {
        while (control != null)
        {
            if (control is Button or TabItem or ListBoxItem or MenuItem or CheckBox or RadioButton)
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
        
        return type;
    }
}
