using System.Globalization;
using Avalonia.Media;

namespace Mostlylucid.Avalonia.UITesting.Svg;

public static class SvgColorHelper
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string BrushToSvgColor(IBrush? brush)
    {
        if (brush is ISolidColorBrush scb)
            return $"#{scb.Color.R:x2}{scb.Color.G:x2}{scb.Color.B:x2}";
        return "none";
    }

    public static double BrushOpacity(IBrush? brush)
    {
        if (brush is ISolidColorBrush scb)
            return scb.Opacity * (scb.Color.A / 255.0);
        return 1.0;
    }

    public static string ThicknessToStrokeWidth(global::Avalonia.Thickness t)
    {
        var max = Math.Max(Math.Max(t.Left, t.Right), Math.Max(t.Top, t.Bottom));
        return max.ToString("F1", Inv);
    }

    public static string F(double value) => value.ToString(Inv);
    public static string F2(double value) => value.ToString("F2", Inv);
}