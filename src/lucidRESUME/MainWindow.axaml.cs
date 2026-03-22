using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
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

/// <summary>Converts status text to a traffic-light color for the status dot.</summary>
public sealed class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F9E2AF"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F38BA8"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#6C7086"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (s.Contains("Connected", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("local", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
            return Green;
        if (s.Contains("Offline", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("no model", StringComparison.OrdinalIgnoreCase))
            return Red;
        if (s.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            return Grey;
        if (s == "...")
            return Yellow;
        return Yellow;
    }

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
        Loaded += async (_, _) => await vm.InitAsync();
    }
}
