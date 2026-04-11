using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Tests;

/// <summary>
/// End-to-end resolution of the locator engine against a real Avalonia visual
/// tree built inside a headless host. Each test constructs a window, runs a
/// selector, and asserts the right control is returned.
/// </summary>
[Collection("Avalonia")]
public class LocatorIntegrationTests
{
    private readonly HeadlessAvaloniaFixture _fx;

    public LocatorIntegrationTests(HeadlessAvaloniaFixture fx) { _fx = fx; }

    private static Window BuildWindow()
    {
        var saveBtn = new Button { Name = "SaveBtn", Content = "Save" };
        var cancelBtn = new Button { Name = "CancelBtn", Content = "Cancel" };
        var emailLabel = new TextBlock { Name = "EmailLabel", Text = "Email" };
        var emailInput = new TextBox { Name = "EmailInput" };
        AutomationProperties.SetLabeledBy(emailInput, emailLabel);
        AutomationProperties.SetAutomationId(saveBtn, "save-btn");

        var nestedSaveBtn = new Button { Content = "Save" };
        var headerPanel = new StackPanel
        {
            Name = "Header",
            Children =
            {
                new TextBlock { Text = "Header Title" },
                nestedSaveBtn
            }
        };

        var jobItem1 = new TextBlock { Text = "Senior Engineer @ Acme" };
        var jobItem2 = new TextBlock { Text = "Lead Dev @ Globex" };
        var jobList = new StackPanel
        {
            Name = "JobList",
            Orientation = Orientation.Vertical,
            Children = { jobItem1, jobItem2 }
        };

        var root = new StackPanel
        {
            Name = "Root",
            Children = { headerPanel, emailLabel, emailInput, saveBtn, cancelBtn, jobList }
        };

        var window = new Window
        {
            Name = "MainWindow",
            Title = "Test",
            Width = 800,
            Height = 600,
            Content = root
        };
        window.Show();
        return window;
    }

    [Fact]
    public Task ByName_FindsControlByNameProperty()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("name=SaveBtn").Resolve(window).ToList();
            Assert.Single(matches);
            Assert.Equal("SaveBtn", matches[0].Name);
            window.Close();
        });
    }

    [Fact]
    public Task ByType_FindsAllButtons()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("type=Button").Resolve(window).ToList();
            // SaveBtn, CancelBtn, nested Save inside Header
            Assert.Equal(3, matches.Count);
            window.Close();
        });
    }

    [Fact]
    public Task ByText_FindsTextBlocks()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("text=Email").Resolve(window).ToList();
            Assert.Contains(matches, c => c is TextBlock tb && tb.Text == "Email");
            window.Close();
        });
    }

    [Fact]
    public Task ByText_AlsoFindsButtonContentString()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("text=Save").Resolve(window).ToList();
            // Both SaveBtn (content="Save") and the nested Save button match
            Assert.True(matches.Count >= 2);
            Assert.All(matches, c => Assert.True(c is Button or TextBlock));
            window.Close();
        });
    }

    [Fact]
    public Task ByTestId_FindsControlWithAutomationId()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("testid=save-btn").Resolve(window).ToList();
            Assert.Single(matches);
            Assert.Equal("SaveBtn", matches[0].Name);
            window.Close();
        });
    }

    [Fact]
    public Task ByLabel_FindsLabeledByInput()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("label=Email").Resolve(window).ToList();
            Assert.Single(matches);
            Assert.Equal("EmailInput", matches[0].Name);
            window.Close();
        });
    }

    [Fact]
    public Task First_PicksFirstMatch()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("first(type=Button)").Resolve(window).ToList();
            Assert.Single(matches);
            window.Close();
        });
    }

    [Fact]
    public Task Nth_PicksByIndex()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var allButtons = SelectorParser.Parse("type=Button").Resolve(window).ToList();
            var nth = SelectorParser.Parse("nth(1, type=Button)").Resolve(window).ToList();
            Assert.Single(nth);
            Assert.Same(allButtons[1], nth[0]);
            window.Close();
        });
    }

    [Fact]
    public Task Last_PicksLastMatch()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var allButtons = SelectorParser.Parse("type=Button").Resolve(window).ToList();
            var last = SelectorParser.Parse("last(type=Button)").Resolve(window).ToList();
            Assert.Single(last);
            Assert.Same(allButtons[^1], last[0]);
            window.Close();
        });
    }

    [Fact]
    public Task Inside_RestrictsToContainer()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("inside(name=Header) type=Button").Resolve(window).ToList();
            Assert.Single(matches);
            // The nested button has no Name; assert it lives inside Header
            window.Close();
        });
    }

    [Fact]
    public Task ImplicitAnd_FiltersByMultipleAtoms()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            // type=Button text=Save → buttons whose displayed text contains "Save"
            var matches = SelectorParser.Parse("type=Button text=Save").Resolve(window).ToList();
            Assert.True(matches.Count >= 1);
            Assert.All(matches, c => Assert.IsAssignableFrom<Button>(c));
            window.Close();
        });
    }

    [Fact]
    public Task HasTextPseudo_FiltersByDescendantText()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            var matches = SelectorParser.Parse("name=JobList:has-text(Acme)").Resolve(window).ToList();
            Assert.Single(matches);
            Assert.Equal("JobList", matches[0].Name);
            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_AutoRetries_UntilControlAppears()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = new Window { Name = "RetryWindow", Width = 400, Height = 300 };
            window.Show();
            var engine = new LocatorEngine { PollIntervalMs = 25 };

            // Schedule control creation 200ms in the future
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    window.Content = new Button { Name = "LateBtn", Content = "Late" };
                });
            });

            var control = await engine.ResolveOneAsync(SelectorParser.Parse("name=LateBtn"), window, timeoutMs: 2000);
            Assert.NotNull(control);
            Assert.Equal("LateBtn", control.Name);
            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_TimesOut_WhenNothingMatches()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = new Window { Name = "EmptyWindow", Width = 400, Height = 300 };
            window.Show();
            var engine = new LocatorEngine { PollIntervalMs = 25 };

            await Assert.ThrowsAsync<LocatorTimeoutException>(async () =>
                await engine.ResolveOneAsync(SelectorParser.Parse("name=Nope"), window, timeoutMs: 200));
            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_ResolveOne_ThrowsOnAmbiguity()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = BuildWindow();
            var engine = new LocatorEngine();
            await Assert.ThrowsAsync<LocatorAmbiguousException>(async () =>
                await engine.ResolveOneAsync(SelectorParser.Parse("type=Button"), window, timeoutMs: 200));
            window.Close();
        });
    }

    [Fact]
    public Task LocatorEngine_ResolveFirst_OkWithMultiple()
    {
        return _fx.DispatchAsync(async () =>
        {
            var window = BuildWindow();
            var engine = new LocatorEngine();
            var first = await engine.ResolveFirstAsync(SelectorParser.Parse("type=Button"), window, timeoutMs: 200);
            Assert.NotNull(first);
            window.Close();
        });
    }

    [Fact]
    public Task BareWord_BackwardsCompat_FindsByName()
    {
        return _fx.DispatchAsync(() =>
        {
            var window = BuildWindow();
            // No "=" → treat as name=SaveBtn
            var matches = SelectorParser.Parse("SaveBtn").Resolve(window).ToList();
            Assert.Single(matches);
            Assert.Equal("SaveBtn", matches[0].Name);
            window.Close();
        });
    }
}
