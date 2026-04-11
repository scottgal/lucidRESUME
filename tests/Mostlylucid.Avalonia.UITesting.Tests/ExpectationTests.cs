using Avalonia.Controls;
using Avalonia.Threading;
using Mostlylucid.Avalonia.UITesting.Expect;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Tests;

[Collection("Avalonia")]
public class ExpectationTests
{
    private readonly HeadlessAvaloniaFixture _fx;

    public ExpectationTests(HeadlessAvaloniaFixture fx) { _fx = fx; }

    [Fact]
    public Task PassesImmediately_WhenStateAlreadyMatches()
    {
        return _fx.DispatchAsync(async () =>
        {
            var status = new TextBlock { Name = "Status", Text = "Saved" };
            var window = new Window { Content = status, Width = 400, Height = 300 };
            window.Show();

            var expectation = new Expectation(By.Name("Status"), new HasTextMatcher("Saved"))
            {
                TimeoutMs = 500
            };
            await expectation.AssertAsync(window);
            window.Close();
        });
    }

    [Fact]
    public Task AutoWaits_UntilTextChanges()
    {
        return _fx.DispatchAsync(async () =>
        {
            var status = new TextBlock { Name = "Status", Text = "Loading" };
            var window = new Window { Content = status, Width = 400, Height = 300 };
            window.Show();

            // Mutate after 200ms
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                await Dispatcher.UIThread.InvokeAsync(() => status.Text = "Saved");
            });

            var expectation = new Expectation(By.Name("Status"), new HasTextMatcher("Saved"))
            {
                TimeoutMs = 2000,
                PollIntervalMs = 25
            };
            await expectation.AssertAsync(window);
            window.Close();
        });
    }

    [Fact]
    public Task AutoWaits_UntilControlAppears()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = new Window { Width = 400, Height = 300 };
            window.Show();

            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    window.Content = new TextBlock { Name = "LateLabel", Text = "I'm here" };
                });
            });

            var expectation = new Expectation(By.Name("LateLabel"), new IsVisibleMatcher())
            {
                TimeoutMs = 2000,
                PollIntervalMs = 25
            };
            await expectation.AssertAsync(window);
            window.Close();
        });
    }

    [Fact]
    public Task TimesOut_WhenMatcherNeverHolds()
    {
        return _fx.DispatchAsync(async () =>
        {
            var status = new TextBlock { Name = "Status", Text = "Loading" };
            var window = new Window { Content = status, Width = 400, Height = 300 };
            window.Show();

            var expectation = new Expectation(By.Name("Status"), new HasTextMatcher("Saved"))
            {
                TimeoutMs = 200,
                PollIntervalMs = 25
            };

            var ex = await Assert.ThrowsAsync<ExpectTimeoutException>(() => expectation.AssertAsync(window));
            Assert.Contains("Status", ex.LocatorDescription);
            Assert.Contains("Saved", ex.LastDetail ?? "");
            window.Close();
        });
    }

    [Fact]
    public Task TimesOut_WhenLocatorNeverResolves()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = new Window { Width = 400, Height = 300 };
            window.Show();

            var expectation = new Expectation(By.Name("Nope"), new IsVisibleMatcher())
            {
                TimeoutMs = 150,
                PollIntervalMs = 25
            };
            await Assert.ThrowsAsync<ExpectTimeoutException>(() => expectation.AssertAsync(window));
            window.Close();
        });
    }

    [Fact]
    public Task NotMatcher_PassesWhenInverseHolds()
    {
        return _fx.DispatchAsync(async () =>
        {
            var btn = new Button { Name = "Btn", IsEnabled = false };
            var window = new Window { Content = btn, Width = 400, Height = 300 };
            window.Show();

            var expectation = new Expectation(By.Name("Btn"), new IsEnabledMatcher().Not())
            {
                TimeoutMs = 200
            };
            await expectation.AssertAsync(window);
            window.Close();
        });
    }

    [Fact]
    public Task ExpectBuilder_FluentApi_Works()
    {
        return _fx.DispatchAsync(async () =>
        {
            var status = new TextBlock { Name = "Status", Text = "Saved" };
            var btn = new Button { Name = "SaveBtn", IsEnabled = true };
            var panel = new StackPanel { Children = { status, btn } };
            var window = new Window { Content = panel, Width = 400, Height = 300 };
            window.Show();

            var engine = new LocatorEngine();
            Task Run(Expectation e) => e.AssertAsync(window, engine);

            await new ExpectBuilder(By.Name("Status"), Run).ToHaveText("Saved");
            await new ExpectBuilder(By.Name("Status"), Run).ToContainText("Sav");
            await new ExpectBuilder(By.Name("SaveBtn"), Run).ToBeEnabled();
            await new ExpectBuilder(By.Name("SaveBtn"), Run).Not.ToBeDisabled();
            window.Close();
        });
    }

    [Fact]
    public Task ExpectBuilder_TimeoutOverride()
    {
        return _fx.DispatchAsync(async () =>
        {
            var status = new TextBlock { Name = "Status", Text = "Loading" };
            var window = new Window { Content = status, Width = 400, Height = 300 };
            window.Show();

            var engine = new LocatorEngine();
            Task Run(Expectation e) => e.AssertAsync(window, engine);

            await Assert.ThrowsAsync<ExpectTimeoutException>(async () =>
                await new ExpectBuilder(By.Name("Status"), Run).WithTimeout(100).ToHaveText("Saved"));
            window.Close();
        });
    }
}
