using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using lucidRESUME.ViewModels;

namespace lucidRESUME;

/// <summary>Converts SelectedNav string to bool by comparing to ConverterParameter.</summary>
public sealed class NavIsActiveConverter : IValueConverter
{
    public static readonly NavIsActiveConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && parameter is string p && string.Equals(s, p, StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    // Parameterless constructor for Avalonia designer / XAML runtime loader
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }
}
