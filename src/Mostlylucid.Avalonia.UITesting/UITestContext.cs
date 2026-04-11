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

    public void EnableCrossWindowTracking()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) => ClearTrackedWindows();
        }

        if (MainWindow != null && !_trackedWindows.Contains(MainWindow))
        {
            lock (_windowLock) _trackedWindows.Add(MainWindow);
        }

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            lock (_windowLock)
            {
                if (!_trackedWindows.Contains(window))
                    _trackedWindows.Add(window);
            }
            WindowOpened?.Invoke(this, window);
        });

        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) =>
        {
            lock (_windowLock) _trackedWindows.Remove(window);
            WindowClosed?.Invoke(this, window);
        });
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
        var convertedValue = value == null ? null : Convert.ChangeType(value, targetType);
        finalProp.SetValue(current, convertedValue);
        return true;
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
