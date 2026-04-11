using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Mostlylucid.Avalonia.UITesting.Players;

/// <summary>
/// Centralized window/region/control capture. Renders the full window once via
/// <see cref="RenderTargetBitmap"/>, then crops via SkiaSharp when a snip rect or
/// control target is supplied. Useful for documentation/manuals where you want
/// just one button or panel rather than the whole window.
/// </summary>
public static class ScreenshotCapture
{
    /// <summary>Capture the entire window to <paramref name="filePath"/>.</summary>
    public static Task<string> CaptureWindowAsync(Window window, string filePath)
        => CaptureAsync(window, filePath, region: null);

    /// <summary>
    /// Capture a rectangular region of the window in DIPs. The rect is clamped to
    /// the window's bounds.
    /// </summary>
    public static Task<string> CaptureRegionAsync(Window window, string filePath, Rect region)
        => CaptureAsync(window, filePath, region);

    /// <summary>
    /// Capture the bounds of <paramref name="control"/> within <paramref name="window"/>,
    /// optionally inflated by <paramref name="padding"/> DIPs on every side.
    /// </summary>
    public static async Task<string> CaptureControlAsync(Window window, Control control, string filePath, double padding = 0)
    {
        var rect = await Dispatcher.UIThread.InvokeAsync(() => GetControlBoundsInWindow(control, window, padding));
        return await CaptureAsync(window, filePath, rect);
    }

    /// <summary>
    /// Capture the bounding box of multiple controls (handy for snipping a group:
    /// e.g. a label + textbox + button shown together in a manual).
    /// </summary>
    public static async Task<string> CaptureControlsAsync(Window window, IEnumerable<Control> controls, string filePath, double padding = 0)
    {
        var union = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Rect? acc = null;
            foreach (var c in controls)
            {
                var r = GetControlBoundsInWindow(c, window, 0);
                acc = acc is null ? r : acc.Value.Union(r);
            }
            if (acc is null) throw new InvalidOperationException("CaptureControls requires at least one control");
            return acc.Value.Inflate(padding);
        });
        return await CaptureAsync(window, filePath, union);
    }

    /// <summary>
    /// Compute the position and size of a control in window-coordinate DIPs,
    /// optionally inflated by <paramref name="padding"/>. Must be called on the UI thread.
    /// </summary>
    public static Rect GetControlBoundsInWindow(Control control, Window window, double padding = 0)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
        var rect = new Rect(topLeft.X, topLeft.Y, control.Bounds.Width, control.Bounds.Height);
        return padding > 0 ? rect.Inflate(padding) : rect;
    }

    private static async Task<string> CaptureAsync(Window window, string filePath, Rect? region)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        var tcs = new TaskCompletionSource<string>();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                window.UpdateLayout();

                var winW = Math.Max(1, (int)Math.Ceiling(window.Bounds.Width));
                var winH = Math.Max(1, (int)Math.Ceiling(window.Bounds.Height));

                using var fullBitmap = new RenderTargetBitmap(new PixelSize(winW, winH), new Vector(96, 96));
                fullBitmap.Render(window);

                if (region is null)
                {
                    using var stream = File.Create(filePath);
                    fullBitmap.Save(stream);
                    tcs.SetResult(filePath);
                    return;
                }

                // Clamp the requested region to the rendered bitmap.
                var winRect = new Rect(0, 0, winW, winH);
                var clamped = region.Value.Intersect(winRect);
                if (clamped.Width <= 0 || clamped.Height <= 0)
                    throw new InvalidOperationException(
                        $"Snip region {region.Value} is outside window bounds {winRect}");

                // Round to integer pixel coordinates so the crop lands on real pixels.
                var x = (int)Math.Floor(clamped.X);
                var y = (int)Math.Floor(clamped.Y);
                var w = Math.Max(1, (int)Math.Ceiling(clamped.Right) - x);
                var h = Math.Max(1, (int)Math.Ceiling(clamped.Bottom) - y);

                // Round-trip the Avalonia bitmap through PNG so we can hand it to SkiaSharp.
                using var ms = new MemoryStream();
                fullBitmap.Save(ms);
                ms.Position = 0;

                using var fullSk = SKBitmap.Decode(ms)
                    ?? throw new InvalidOperationException("Failed to decode rendered window bitmap for snipping");

                // Clamp again against the actual decoded bitmap size in case DPI scaling shifted it.
                w = Math.Min(w, fullSk.Width - x);
                h = Math.Min(h, fullSk.Height - y);
                if (w <= 0 || h <= 0)
                    throw new InvalidOperationException("Snip region collapsed to empty after clamping to bitmap");

                using var cropped = new SKBitmap(w, h, fullSk.ColorType, fullSk.AlphaType);
                if (!fullSk.ExtractSubset(cropped, new SKRectI(x, y, x + w, y + h)))
                    throw new InvalidOperationException("SKBitmap.ExtractSubset failed");

                using var image = SKImage.FromBitmap(cropped);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var fs = File.Create(filePath);
                data.SaveTo(fs);

                tcs.SetResult(filePath);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Render);

        return await tcs.Task;
    }
}
