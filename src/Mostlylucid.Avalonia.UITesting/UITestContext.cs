using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting;

public sealed class UITestContext
{
    private readonly List<Window> _trackedWindows = new();
    private readonly object _windowLock = new();

    public Window? MainWindow { get; set; }
    public IServiceProvider? Services { get; set; }
    public Action<string>? Navigate { get; set; }

    public IReadOnlyList<Window> TrackedWindows
    {
        get { lock (_windowLock) return _trackedWindows.ToList().AsReadOnly(); }
    }

    // Class handlers on Window.WindowOpenedEvent / WindowClosedEvent are
    // process-wide and cannot be removed via the AddClassHandler API; if
    // each EnableCrossWindowTracking() call added its own handler, every
    // script run / new session would leak another listener (and the
    // captured `this`). Instead we install ONE class handler ever (gated
    // by _classHandlersInstalled) that fans out to every live context
    // registered in _activeContexts. Per-instance state is unchanged.
    private static readonly object _staticLock = new();
    private static bool _classHandlersInstalled;
    private static readonly List<WeakReference<UITestContext>> _activeContexts = new();

    public void EnableCrossWindowTracking()
    {
        if (MainWindow != null)
        {
            lock (_windowLock)
                if (!_trackedWindows.Contains(MainWindow))
                    _trackedWindows.Add(MainWindow);
        }

        lock (_staticLock)
        {
            // Compact dead references and register this context.
            _activeContexts.RemoveAll(wr => !wr.TryGetTarget(out _));
            if (!_activeContexts.Any(wr => wr.TryGetTarget(out var ctx) && ReferenceEquals(ctx, this)))
                _activeContexts.Add(new WeakReference<UITestContext>(this));

            if (_classHandlersInstalled) return;
            _classHandlersInstalled = true;

            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownRequested += (_, _) =>
                {
                    lock (_staticLock)
                        foreach (var wr in _activeContexts)
                            if (wr.TryGetTarget(out var ctx)) ctx.ClearTrackedWindows();
                };
            }

            Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
                FanOut(ctx => ctx.OnWindowOpened(window)));
            Window.WindowClosedEvent.AddClassHandler<Window>((window, _) =>
                FanOut(ctx => ctx.OnWindowClosed(window)));
        }
    }

    private static void FanOut(Action<UITestContext> action)
    {
        List<WeakReference<UITestContext>> snapshot;
        lock (_staticLock) snapshot = _activeContexts.ToList();
        foreach (var wr in snapshot)
            if (wr.TryGetTarget(out var ctx)) action(ctx);
    }

    private void OnWindowOpened(Window window)
    {
        lock (_windowLock)
        {
            if (!_trackedWindows.Contains(window))
                _trackedWindows.Add(window);
        }
        WindowOpened?.Invoke(this, window);
    }

    private void OnWindowClosed(Window window)
    {
        lock (_windowLock) _trackedWindows.Remove(window);
        WindowClosed?.Invoke(this, window);
    }

    private void ClearTrackedWindows()
    {
        lock (_windowLock) _trackedWindows.Clear();
    }

    public event EventHandler<Window>? WindowOpened;
    public event EventHandler<Window>? WindowClosed;

    public object? GetService(Type type) => Services?.GetService(type);
    public T? GetService<T>() where T : class => Services?.GetService<T>();

    /// <summary>
    /// Resolve a selector string to the first matching control. Accepts the full
    /// Playwright-style locator grammar from <see cref="SelectorParser"/>:
    /// <c>name=SaveBtn</c>, <c>type=Button text=Save</c>, <c>role=button</c>,
    /// <c>testid=save-btn</c>, etc. A bare word with no operators is treated as
    /// <c>name=...</c> for backwards compatibility.
    ///
    /// This is a synchronous one-shot lookup with no retry. Use
    /// <see cref="LocatorEngine.ResolveFirstAsync(Locator, Avalonia.Controls.Control, int?)"/>
    /// for auto-waiting resolution in interactions.
    /// </summary>
    public Control? FindControl(string? selector, Window? window = null)
    {
        var target = window ?? MainWindow;
        if (string.IsNullOrEmpty(selector) || target == null) return null;
        try
        {
            var locator = SelectorParser.Parse(selector);
            return locator.Resolve(target).FirstOrDefault();
        }
        catch (SelectorParseException)
        {
            return null;
        }
    }

    public IEnumerable<Control> GetAllControls(Window? window = null)
    {
        var target = window ?? MainWindow;
        if (target == null) return Enumerable.Empty<Control>();
        return FindControlsRecursive(target);
    }

    private static IEnumerable<Control> FindControlsRecursive(Control root)
    {
        yield return root;

        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control c)
                {
                    foreach (var desc in FindControlsRecursive(c))
                        yield return desc;
                }
            }
        }
        else if (root is ContentControl cc && cc.Content is Control content)
        {
            foreach (var desc in FindControlsRecursive(content))
                yield return desc;
        }
        else if (root is Decorator d && d.Child is Control child)
        {
            foreach (var desc in FindControlsRecursive(child))
                yield return desc;
        }
        else if (root is ItemsControl ic)
        {
            var presenter = ic.Presenter;
            if (presenter is Control presenterControl)
            {
                foreach (var desc in FindControlsRecursive(presenterControl))
                    yield return desc;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Walks dotted property paths on consumer-defined ViewModels via reflection. Under PublishTrimmed/PublishAot the trimmer may remove properties whose only access path is here.")]
    public object? GetProperty(object target, string propertyPath)
    {
        var parts = propertyPath.Split('.');
        object? current = target;

        foreach (var part in parts)
        {
            if (current == null) return null;

            var type = current.GetType();
            var prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop == null)
            {
                var field = type.GetField(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null) return $"Property not found: {part}";
                current = field.GetValue(current);
            }
            else
            {
                current = prop.GetValue(current);
            }
        }

        return current;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Walks dotted property paths on consumer-defined ViewModels via reflection. Under PublishTrimmed/PublishAot the trimmer may remove properties whose only access path is here.")]
    public bool SetProperty(object target, string propertyPath, object? value)
    {
        var parts = propertyPath.Split('.');
        if (parts.Length == 0) return false;

        object? current = target;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return false;
            var prop = current.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return false;
            current = prop.GetValue(current);
        }

        if (current == null) return false;

        var finalProp = current.GetType().GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (finalProp == null || !finalProp.CanWrite) return false;

        var targetType = Nullable.GetUnderlyingType(finalProp.PropertyType) ?? finalProp.PropertyType;
        var convertedValue = ConvertForProperty(value, targetType);
        finalProp.SetValue(current, convertedValue);
        return true;
    }

    private static object? ConvertForProperty(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Convert.ChangeType refuses enums and only handles IConvertible
        // targets; scripts (YAML/JSON) pass enum values as strings, so we
        // handle them here before falling through.
        if (targetType.IsEnum)
        {
            return value is string s
                ? Enum.Parse(targetType, s, ignoreCase: true)
                : Enum.ToObject(targetType, value);
        }
        if (typeof(IConvertible).IsAssignableFrom(targetType))
            return Convert.ChangeType(value, targetType);

        throw new InvalidOperationException(
            $"Cannot convert value of type {value.GetType().Name} to property type {targetType.Name}. " +
            "Add a custom converter or pass the value pre-converted.");
    }

    public async Task<T> RunOnUIThreadAsync<T>(Func<T> action)
    {
        return await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task RunOnUIThreadAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public Window? FindWindow(string? windowId)
    {
        if (string.IsNullOrEmpty(windowId)) return MainWindow;

        lock (_windowLock)
        {
            return _trackedWindows.FirstOrDefault(w =>
                w.Name == windowId ||
                w.Title == windowId ||
                w.GetType().Name == windowId);
        }
    }
}
