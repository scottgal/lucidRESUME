using Avalonia.Controls;
using Mostlylucid.Avalonia.UITesting.Expect;

namespace Mostlylucid.Avalonia.UITesting.Tests;

[Collection("Avalonia")]
public class MatcherTests
{
    private readonly HeadlessAvaloniaFixture _fx;

    public MatcherTests(HeadlessAvaloniaFixture fx) { _fx = fx; }

    [Fact]
    public Task IsVisible_PassesForVisibleControl()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { Content = "Save", IsVisible = true };
            Assert.True(new IsVisibleMatcher().Evaluate(btn).Pass);
        });

    [Fact]
    public Task IsVisible_FailsForHiddenControl()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { Content = "Save", IsVisible = false };
            Assert.False(new IsVisibleMatcher().Evaluate(btn).Pass);
        });

    [Fact]
    public Task IsHidden_InverseOfIsVisible()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { IsVisible = false };
            Assert.True(new IsHiddenMatcher().Evaluate(btn).Pass);
        });

    [Fact]
    public Task IsEnabled_PassesForEnabledControl()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { IsEnabled = true };
            Assert.True(new IsEnabledMatcher().Evaluate(btn).Pass);
        });

    [Fact]
    public Task IsDisabled_PassesForDisabledControl()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { IsEnabled = false };
            Assert.True(new IsDisabledMatcher().Evaluate(btn).Pass);
        });

    [Fact]
    public Task IsChecked_PassesForCheckedCheckBox()
        => _fx.DispatchAsync(() =>
        {
            var cb = new CheckBox { IsChecked = true };
            Assert.True(new IsCheckedMatcher().Evaluate(cb).Pass);
        });

    [Fact]
    public Task IsChecked_FailsForUncheckedCheckBox()
        => _fx.DispatchAsync(() =>
        {
            var cb = new CheckBox { IsChecked = false };
            Assert.False(new IsCheckedMatcher().Evaluate(cb).Pass);
        });

    [Fact]
    public Task IsUnchecked_PassesForRadioButton()
        => _fx.DispatchAsync(() =>
        {
            var rb = new RadioButton { IsChecked = false };
            Assert.True(new IsUncheckedMatcher().Evaluate(rb).Pass);
        });

    [Fact]
    public Task IsChecked_FailsOnNonToggleable()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button();
            var result = new IsCheckedMatcher().Evaluate(btn);
            Assert.False(result.Pass);
            Assert.Contains("no IsChecked", result.Detail);
        });

    [Fact]
    public Task HasText_ExactMatch()
        => _fx.DispatchAsync(() =>
        {
            var tb = new TextBlock { Text = "Saved" };
            Assert.True(new HasTextMatcher("Saved", exact: true).Evaluate(tb).Pass);
            Assert.False(new HasTextMatcher("Save", exact: true).Evaluate(tb).Pass);
        });

    [Fact]
    public Task HasText_FromTextBox()
        => _fx.DispatchAsync(() =>
        {
            var tb = new TextBox { Text = "scott@example.com" };
            Assert.True(new HasTextMatcher("scott@example.com").Evaluate(tb).Pass);
        });

    [Fact]
    public Task ContainsText_SubstringMatch()
        => _fx.DispatchAsync(() =>
        {
            var tb = new TextBlock { Text = "Saved at 12:34" };
            Assert.True(new ContainsTextMatcher("Saved").Evaluate(tb).Pass);
            Assert.True(new ContainsTextMatcher("12:34").Evaluate(tb).Pass);
            Assert.False(new ContainsTextMatcher("nope").Evaluate(tb).Pass);
        });

    [Fact]
    public Task MatchesRegex_Pattern()
        => _fx.DispatchAsync(() =>
        {
            var tb = new TextBlock { Text = "Order #1234 placed" };
            Assert.True(new MatchesRegexMatcher(@"#\d+").Evaluate(tb).Pass);
            Assert.False(new MatchesRegexMatcher(@"^\d+$").Evaluate(tb).Pass);
        });

    [Fact]
    public Task HasCount_ItemsControl()
        => _fx.DispatchAsync(() =>
        {
            var lb = new ListBox { ItemsSource = new[] { "a", "b", "c" } };
            Assert.True(new HasCountMatcher(3).Evaluate(lb).Pass);
            Assert.False(new HasCountMatcher(5).Evaluate(lb).Pass);
        });

    [Fact]
    public Task HasCount_PanelChildren()
        => _fx.DispatchAsync(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new Button());
            panel.Children.Add(new TextBlock());
            Assert.True(new HasCountMatcher(2).Evaluate(panel).Pass);
        });

    [Fact]
    public Task HasValue_TextBox()
        => _fx.DispatchAsync(() =>
        {
            var tb = new TextBox { Text = "hello" };
            Assert.True(new HasValueMatcher("hello").Evaluate(tb).Pass);
        });

    [Fact]
    public Task HasValue_Slider()
        => _fx.DispatchAsync(() =>
        {
            var s = new Slider { Minimum = 0, Maximum = 100, Value = 42 };
            Assert.True(new HasValueMatcher("42").Evaluate(s).Pass);
        });

    [Fact]
    public Task HasProperty_ByReflection()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { Content = "Save" };
            Assert.True(new HasPropertyMatcher("Content", "Save").Evaluate(btn).Pass);
            Assert.False(new HasPropertyMatcher("Content", "Cancel").Evaluate(btn).Pass);
        });

    [Fact]
    public Task NotMatcher_Inverts()
        => _fx.DispatchAsync(() =>
        {
            var btn = new Button { IsEnabled = false };
            Assert.True(new IsEnabledMatcher().Not().Evaluate(btn).Pass);
            Assert.False(new IsDisabledMatcher().Not().Evaluate(btn).Pass);
        });
}
