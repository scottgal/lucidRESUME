using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public sealed class StageToBrushConverter : IValueConverter
{
    public static readonly StageToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ApplicationStage stage) return Brushes.Gray;

        var key = stage switch
        {
            ApplicationStage.Saved => "LrSubtleText",
            ApplicationStage.Applied => "LrAccentBlue",
            ApplicationStage.Screening => "LrAccentYellow",
            ApplicationStage.Interview => "LrAccentPurple",
            ApplicationStage.Offer => "LrAccentGreen",
            ApplicationStage.Accepted => "LrAccentGreen",
            ApplicationStage.Rejected => "LrAccentRed",
            ApplicationStage.Withdrawn => "LrSubtleText",
            ApplicationStage.Ghosted => "LrSubtleText",
            _ => "LrSubtleText"
        };

        return Application.Current?.FindResource(key) is Color c
            ? new SolidColorBrush(c)
            : Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class PipelinePage : UserControl
{
    public PipelinePage()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PipelinePageViewModel vm)
            await vm.LoadAsync();
    }
}
