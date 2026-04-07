using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using lucidRESUME.ViewModels;
using lucidRESUME.ViewModels.Pages;

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
    private static readonly string[] ResumeExtensions = [".pdf", ".docx", ".txt"];
    private static readonly string[] ArchiveExtensions = [".zip"];

    // Parameterless constructor for Avalonia designer / XAML runtime loader
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public MainWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitAsync();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        if (files is null) return;

        // Navigate to Resume page and import each file
        if (DataContext is not MainWindowViewModel mainVm) return;
        var resumeVm = mainVm.GetPage("Resume") as ResumePageViewModel;
        if (resumeVm is null) return;

        mainVm.NavigateCommand.Execute("Resume");

        foreach (var item in files)
        {
            if (item is not Avalonia.Platform.Storage.IStorageFile file) continue;
            var path = file.Path.LocalPath;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ResumeExtensions.Contains(ext) || ArchiveExtensions.Contains(ext))
            {
                await resumeVm.ImportFromPathAsync(path);
            }
        }
    }
}
