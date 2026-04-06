using System.Globalization;
using Avalonia;
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

/// <summary>Converts status text to a traffic-light color for the status dot, using theme resources.</summary>
public sealed class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    private static IBrush Resolve(string key, IBrush fallback) =>
        Application.Current?.FindResource(key) is IBrush b ? b : fallback;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (s.Contains("Connected", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("local", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
            return Resolve("LrStatusGreenBrush", Brushes.Green);
        if (s.Contains("Offline", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("no model", StringComparison.OrdinalIgnoreCase))
            return Resolve("LrStatusRedBrush", Brushes.Red);
        if (s.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            return Resolve("LrStatusGreyBrush", Brushes.Gray);
        if (s == "...")
            return Resolve("LrStatusYellowBrush", Brushes.Yellow);
        return Resolve("LrStatusYellowBrush", Brushes.Yellow);
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
