using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.UXTesting;

public sealed class UXContext
{
    public Window? MainWindow { get; set; }
    public IServiceProvider? Services { get; set; }
    public Action<string>? Navigate { get; set; }
    
    public object? GetService(Type type) => Services?.GetService(type);
    public T? GetService<T>() where T : class => Services?.GetService<T>();
    
    public Control? FindControl(string? name)
    {
        if (string.IsNullOrEmpty(name) || MainWindow == null) return null;
        if (MainWindow.Name == name) return MainWindow;
        return MainWindow.FindControl<Control>(name);
    }
    
    public IEnumerable<Control> GetAllControls()
    {
        if (MainWindow == null) return Enumerable.Empty<Control>();
        return FindControlsRecursive(MainWindow);
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
        
        var convertedValue = Convert.ChangeType(value, finalProp.PropertyType);
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
}
