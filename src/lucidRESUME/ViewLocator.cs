using Avalonia.Controls;
using Avalonia.Controls.Templates;
using lucidRESUME.ViewModels;

namespace lucidRESUME;

public class ViewLocator : IDataTemplate
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Control> _views = new();
    public Control? Build(object? data)
    {
        if (data is null) return null;
        return _views.GetValue(data, CreateView);
    }

    private static Control CreateView(object data)
    {

        var name = data.GetType().FullName!
            .Replace("lucidRESUME.ViewModels.Pages.", "lucidRESUME.Views.Pages.")
            .Replace("lucidRESUME.ViewModels.", "lucidRESUME.Views.")
            .Replace("ViewModel", "");

        var type = Type.GetType(name);
        if (type != null)
            return (Control)Activator.CreateInstance(type)!;

        return new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
