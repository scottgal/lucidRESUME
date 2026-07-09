using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mostlylucid.Avalonia.UITesting.Players;
using Xunit;

namespace Mostlylucid.Avalonia.UITesting.Tests;

[Collection("Avalonia")]
public class HeadlessRenderTests
{
    private readonly HeadlessAvaloniaFixture _fx;
    public HeadlessRenderTests(HeadlessAvaloniaFixture fx) => _fx = fx;

    // SettleAsync drives layout + items-container generation so an ItemsControl's items are
    // materialized in the visual tree, and it leaves the tree stable (idempotent second call).
    [Fact]
    public Task SettleAsync_realizes_items_and_stabilizes()
    {
        return _fx.DispatchAsync(async () =>
        {
            var items = new ItemsControl
            {
                ItemsSource = new[] { "ONE", "TWO", "THREE" },
                ItemTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = s }, true),
            };
            var window = new Window { Width = 300, Height = 200, Content = items };
            // Theme this window so ItemsControl has a template (the shared TestApp is themeless).
            window.Styles.Add(new FluentTheme());
            window.Show();

            await HeadlessRender.SettleAsync(window);

            int Materialized() => window.GetVisualDescendants().OfType<TextBlock>()
                .Count(t => t.Text is "ONE" or "TWO" or "THREE");

            Assert.Equal(3, Materialized());

            // Idempotent: settling an already-settled window changes nothing and does not throw.
            int before = window.GetVisualDescendants().Count();
            await HeadlessRender.SettleAsync(window);
            int after = window.GetVisualDescendants().Count();
            Assert.Equal(before, after);

            window.Close();
        });
    }

    // SettleAsync must be safe when there is nothing to realize (no items, no deferred content).
    [Fact]
    public Task SettleAsync_is_safe_on_a_trivial_window()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = new Window { Width = 120, Height = 80, Content = new TextBlock { Text = "hi" } };
            window.Styles.Add(new FluentTheme());
            window.Show();

            await HeadlessRender.SettleAsync(window);

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "hi");
            window.Close();
        });
    }
}
