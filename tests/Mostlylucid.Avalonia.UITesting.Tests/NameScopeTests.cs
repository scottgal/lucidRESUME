using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Tests;

/// <summary>
/// Regression tests for the locator engine's namescope-independence.
///
/// Avalonia <c>NameScope</c> is per-XAML-file: a UserControl's named children
/// are invisible to the parent Window's <c>FindControl&lt;T&gt;(name)</c>. The
/// locator engine must walk the visual + logical trees directly so it finds
/// controls regardless of which namescope they were registered in.
/// </summary>
[Collection("Avalonia")]
public class NameScopeTests
{
    private readonly HeadlessAvaloniaFixture _fx;

    public NameScopeTests(HeadlessAvaloniaFixture fx) { _fx = fx; }

    /// <summary>
    /// A UserControl that registers its child names in its own namescope —
    /// exactly the situation where Avalonia's <c>FindControl&lt;T&gt;(name)</c>
    /// from the parent Window returns null.
    /// </summary>
    private sealed class NestedUserControl : UserControl
    {
        public NestedUserControl()
        {
            var restartButton = new Button { Name = "RestartButton", Content = "Restart" };
            var statusLabel = new TextBlock { Name = "StatusLabel", Text = "Idle" };
            var panel = new StackPanel { Children = { statusLabel, restartButton } };

            // Establish an isolated namescope and register the children in it,
            // mirroring how XAML-loaded UserControls behave.
            var scope = new NameScope();
            NameScope.SetNameScope(this, scope);
            scope.Register("RestartButton", restartButton);
            scope.Register("StatusLabel", statusLabel);

            Content = panel;
        }
    }

    [Fact]
    public Task LocatorEngine_FindsControlInsideNestedNameScope()
    {
        return _fx.DispatchAsync(async () =>
        {
            var nested = new NestedUserControl();
            var window = new Window { Content = nested, Width = 400, Height = 300 };
            window.Show();

            // Sanity check: Avalonia's namescope-based lookup from the Window
            // returns null even though the control exists.
            var nameScopeLookup = window.FindControl<Button>("RestartButton");
            Assert.Null(nameScopeLookup);

            // The locator engine MUST find it.
            var engine = new LocatorEngine();
            var resolved = await engine.ResolveOneAsync(SelectorParser.Parse("name=RestartButton"), window, timeoutMs: 500);
            Assert.NotNull(resolved);
            Assert.Equal("RestartButton", resolved.Name);
            Assert.IsType<Button>(resolved);

            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_FindsTextBlockInsideNestedNameScope()
    {
        return _fx.DispatchAsync(async () =>
        {
            var nested = new NestedUserControl();
            var window = new Window { Content = nested, Width = 400, Height = 300 };
            window.Show();

            // Same isolation problem on a TextBlock — namescope lookup fails,
            // locator engine succeeds.
            Assert.Null(window.FindControl<TextBlock>("StatusLabel"));

            var engine = new LocatorEngine();
            var resolved = await engine.ResolveOneAsync(SelectorParser.Parse("name=StatusLabel"), window, timeoutMs: 500);
            Assert.NotNull(resolved);
            Assert.Equal("StatusLabel", resolved.Name);

            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_FindsControlByTypeInsideNestedNameScope()
    {
        return _fx.DispatchAsync(async () =>
        {
            var nested = new NestedUserControl();
            var window = new Window { Content = nested, Width = 400, Height = 300 };
            window.Show();

            var engine = new LocatorEngine();
            var btn = await engine.ResolveFirstAsync(SelectorParser.Parse("type=Button"), window, timeoutMs: 500);
            Assert.NotNull(btn);
            Assert.Equal("RestartButton", btn.Name);

            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_FindsControlByTextInsideNestedNameScope()
    {
        return _fx.DispatchAsync(async () =>
        {
            var nested = new NestedUserControl();
            var window = new Window { Content = nested, Width = 400, Height = 300 };
            window.Show();

            var engine = new LocatorEngine();
            var resolved = await engine.ResolveOneAsync(
                SelectorParser.Parse("type=Button:has-text(Restart)"), window, timeoutMs: 500);
            Assert.NotNull(resolved);
            Assert.Equal("RestartButton", resolved.Name);

            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_FindsDoubleNestedControlAcrossTwoNameScopes()
    {
        return _fx.DispatchAsync(async () =>
        {
            // Window → outer UserControl → inner UserControl → button
            var inner = new NestedUserControl();
            var outerScope = new NameScope();
            var outer = new UserControl { Content = inner };
            NameScope.SetNameScope(outer, outerScope);

            var window = new Window { Content = outer, Width = 400, Height = 300 };
            window.Show();

            // Two namescope boundaries between Window and target — namescope
            // lookup returns null at the Window level
            Assert.Null(window.FindControl<Button>("RestartButton"));

            // Locator engine still finds it
            var engine = new LocatorEngine();
            var resolved = await engine.ResolveOneAsync(SelectorParser.Parse("name=RestartButton"), window, timeoutMs: 500);
            Assert.NotNull(resolved);
            Assert.Equal("RestartButton", resolved.Name);

            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_ClicksControlInsideNestedNameScope_EndToEnd()
    {
        return _fx.DispatchAsync(async () =>
        {
            var nested = new NestedUserControl();
            var window = new Window { Content = nested, Width = 400, Height = 300 };
            window.Show();

            var clicked = false;
            var btn = (Button)await new LocatorEngine().ResolveOneAsync(
                SelectorParser.Parse("name=RestartButton"), window, timeoutMs: 500);
            btn.Click += (_, _) => clicked = true;

            // Use the high-level session click path (which uses the locator engine)
            var session = await UITestSession.AttachAsync(window);
            await session.ClickAsync("name=RestartButton");

            Assert.True(clicked, "RestartButton click did not fire — locator missed the namescoped control");
            window.Close();
        });
    }
}
