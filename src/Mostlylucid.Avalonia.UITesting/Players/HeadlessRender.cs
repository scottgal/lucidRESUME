using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Mostlylucid.Avalonia.UITesting.Players;

/// <summary>
/// Drives a headless window through enough of the render/layout loop that deferred, virtualized and
/// animated content actually realizes before a screenshot is taken.
///
/// The headless platform renders on demand (via <c>RenderTargetBitmap</c>) rather than running a
/// continuous compositor frame loop, so content that reveals itself lazily — items-panel containers,
/// controls that defer building their content, reveal animations — may not have materialized when a
/// one-shot capture runs. <see cref="SettleAsync"/> forces layout, generates items containers, ticks
/// the headless render timer, applies any deferred-content controls (by convention, reflectively, so
/// there is no dependency on the control library that provides them), and pumps the dispatcher —
/// repeating until the visual tree stops changing.
/// </summary>
public static class HeadlessRender
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Settle <paramref name="window"/> so deferred/virtualized/animated content is realized.
    /// Safe on non-headless platforms (the render-timer tick is skipped). Returns once the visual
    /// tree stabilizes or <paramref name="maxPasses"/> is reached.
    /// </summary>
    /// <param name="window">The window (or other top level) to settle.</param>
    /// <param name="maxPasses">Upper bound on settle iterations (each is a layout+tick+pump cycle).</param>
    public static async Task SettleAsync(TopLevel window, int maxPasses = 16)
    {
        int previous = -1;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            int count = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var size = new Size(
                    Math.Max(1, window.Bounds.Width),
                    Math.Max(1, window.Bounds.Height));

                window.Measure(size);
                window.Arrange(new Rect(size));

                // Force items presenters to generate their (possibly virtualized) containers.
                foreach (var presenter in window.GetVisualDescendants().OfType<ItemsPresenter>())
                {
                    presenter.Measure(size);
                    presenter.Arrange(new Rect(size));
                }

                ApplyDeferredContent(window);
                TryForceRenderTimerTick();

                return window.GetVisualDescendants().Count();
            });

            // Let posted layout/realization work drain before measuring stability.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (count == previous)
                break; // stabilized — nothing new realized this pass
            previous = count;
        }
    }

    /// <summary>
    /// Realize any deferred-content controls in the tree by convention: a control whose type is named
    /// <c>DeferredContentControl</c> and exposes a parameterless <c>ApplyDeferredPresentation()</c>
    /// method (as Dock.Avalonia's does) is asked to present now. Reflection keeps this dependency-free.
    /// </summary>
    private static void ApplyDeferredContent(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual.GetType().Name != "DeferredContentControl")
                continue;

            var apply = visual.GetType().GetMethod(
                "ApplyDeferredPresentation", Instance, binder: null, Type.EmptyTypes, modifiers: null);
            try { apply?.Invoke(visual, null); }
            catch { /* best-effort: never let a third-party control break settling */ }
        }
    }

    /// <summary>Advance the headless render timer one tick, if we're on the headless platform.</summary>
    private static void TryForceRenderTimerTick()
    {
        try { global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        catch { /* not the headless platform, or API unavailable — ignore */ }
    }
}
