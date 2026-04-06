using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public sealed class ScoreToColorConverter : IValueConverter
{
    public static readonly ScoreToColorConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int score = value is int i ? i : 0;
        var key = score >= 80 ? "LrAccentGreen" : score >= 60 ? "LrAccentYellow" : "LrAccentRed";
        return Application.Current?.FindResource(key) is Avalonia.Media.Color c ? c.ToString() : "#808080";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverted score: low = green (good/human), high = red (bad/AI).</summary>
public sealed class InverseScoreToColorConverter : IValueConverter
{
    public static readonly InverseScoreToColorConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int score = value is int i ? i : 0;
        var key = score <= 25 ? "LrAccentGreen" : score <= 50 ? "LrAccentYellow" : "LrAccentRed";
        return Application.Current?.FindResource(key) is Avalonia.Media.Color c ? c.ToString() : "#808080";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class ResumePage : UserControl
{
    public ResumePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildOpenerFlyout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ResumePageViewModel vm)
            vm.TopLevel = TopLevel.GetTopLevel(this);
        RebuildOpenerFlyout();
    }

    private void RebuildOpenerFlyout()
    {
        if (DataContext is not ResumePageViewModel vm) return;

        var flyout = OpenInButton?.Flyout as MenuFlyout;
        if (flyout == null) return;

        flyout.Items.Clear();
        foreach (var item in vm.OpenerItems)
            flyout.Items.Add(new MenuItem { Header = item.Name, Command = item.Command });
    }
}
