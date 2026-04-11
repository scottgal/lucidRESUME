using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Expect;

// ============================================================================
// Visibility / state matchers
// ============================================================================

public sealed class IsVisibleMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
        => control.IsVisible
            ? MatcherResult.Passed($"IsVisible=true")
            : MatcherResult.Failed($"{Name(control)}.IsVisible was false");
    public override string Describe() => "be visible";
    private static string Name(Control c) => string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name!;
}

public sealed class IsHiddenMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
        => !control.IsVisible
            ? MatcherResult.Passed("IsVisible=false")
            : MatcherResult.Failed($"{Name(control)}.IsVisible was true");
    public override string Describe() => "be hidden";
    private static string Name(Control c) => string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name!;
}

public sealed class IsEnabledMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
        => control.IsEnabled
            ? MatcherResult.Passed("IsEnabled=true")
            : MatcherResult.Failed($"{Name(control)}.IsEnabled was false");
    public override string Describe() => "be enabled";
    private static string Name(Control c) => string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name!;
}

public sealed class IsDisabledMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
        => !control.IsEnabled
            ? MatcherResult.Passed("IsEnabled=false")
            : MatcherResult.Failed($"{Name(control)}.IsEnabled was true");
    public override string Describe() => "be disabled";
    private static string Name(Control c) => string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name!;
}

public sealed class IsCheckedMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
    {
        // ToggleButton is the base for CheckBox / RadioButton / ToggleSwitch.
        var isChecked = control switch
        {
            ToggleButton tb => tb.IsChecked,
            _ => null
        };
        if (isChecked is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no IsChecked property");
        return isChecked == true
            ? MatcherResult.Passed("IsChecked=true")
            : MatcherResult.Failed($"IsChecked was {isChecked.Value}");
    }
    public override string Describe() => "be checked";
}

public sealed class IsUncheckedMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
    {
        var isChecked = control switch
        {
            ToggleButton tb => tb.IsChecked,
            _ => null
        };
        if (isChecked is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no IsChecked property");
        return isChecked != true
            ? MatcherResult.Passed("IsChecked=false")
            : MatcherResult.Failed("IsChecked was true");
    }
    public override string Describe() => "be unchecked";
}

public sealed class IsFocusedMatcher : Matcher
{
    public override MatcherResult Evaluate(Control control)
        => control.IsFocused
            ? MatcherResult.Passed("IsFocused=true")
            : MatcherResult.Failed($"control was not focused");
    public override string Describe() => "be focused";
}

// ============================================================================
// Text matchers
// ============================================================================

public class HasTextMatcher : Matcher
{
    public string Expected { get; }
    public bool Exact { get; }

    public HasTextMatcher(string expected, bool exact = true)
    {
        Expected = expected;
        Exact = exact;
    }

    public override MatcherResult Evaluate(Control control)
    {
        var actual = TextLocator.GetDisplayedText(control);
        if (actual is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no displayed text");
        var match = Exact ? actual == Expected : actual.Contains(Expected, StringComparison.Ordinal);
        return match
            ? MatcherResult.Passed($"text={Quote(actual)}")
            : MatcherResult.Failed($"expected text {(Exact ? "=" : "contains")} {Quote(Expected)}, got {Quote(actual)}");
    }

    public override string Describe()
        => Exact ? $"have text {Quote(Expected)}" : $"contain text {Quote(Expected)}";

    private static string Quote(string s) => $"\"{s}\"";
}

public sealed class ContainsTextMatcher : HasTextMatcher
{
    public ContainsTextMatcher(string expected) : base(expected, exact: false) { }
}

public sealed class MatchesRegexMatcher : Matcher
{
    public Regex Pattern { get; }
    public string PatternSource { get; }

    public MatchesRegexMatcher(string pattern)
    {
        PatternSource = pattern;
        Pattern = new Regex(pattern, RegexOptions.Compiled);
    }

    public override MatcherResult Evaluate(Control control)
    {
        var actual = TextLocator.GetDisplayedText(control);
        if (actual is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no displayed text");
        return Pattern.IsMatch(actual)
            ? MatcherResult.Passed($"text=\"{actual}\" matched /{PatternSource}/")
            : MatcherResult.Failed($"text \"{actual}\" did not match /{PatternSource}/");
    }

    public override string Describe() => $"match /{PatternSource}/";
}

// ============================================================================
// Collection matchers
// ============================================================================

public sealed class HasCountMatcher : Matcher
{
    public int Expected { get; }
    public HasCountMatcher(int expected) { Expected = expected; }

    public override MatcherResult Evaluate(Control control)
    {
        var count = control switch
        {
            ItemsControl ic => ic.ItemCount,
            Panel p => p.Children.Count,
            _ => -1
        };
        if (count < 0)
            return MatcherResult.Failed($"{control.GetType().Name} has no item collection");
        return count == Expected
            ? MatcherResult.Passed($"count={count}")
            : MatcherResult.Failed($"expected count={Expected}, got {count}");
    }

    public override string Describe() => $"have count {Expected}";
}

// ============================================================================
// Property matchers
// ============================================================================

public sealed class HasValueMatcher : Matcher
{
    public string Expected { get; }
    public HasValueMatcher(string expected) { Expected = expected; }

    public override MatcherResult Evaluate(Control control)
    {
        var actual = control switch
        {
            TextBox tb => tb.Text,
            NumericUpDown nud => nud.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Slider s => s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ComboBox cb => cb.SelectedItem?.ToString(),
            _ => null
        };
        if (actual is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no value-like property");
        return actual == Expected
            ? MatcherResult.Passed($"value=\"{actual}\"")
            : MatcherResult.Failed($"expected value=\"{Expected}\", got \"{actual}\"");
    }

    public override string Describe() => $"have value \"{Expected}\"";
}

public sealed class HasPropertyMatcher : Matcher
{
    public string PropertyName { get; }
    public string Expected { get; }

    public HasPropertyMatcher(string propertyName, string expected)
    {
        PropertyName = propertyName;
        Expected = expected;
    }

    public override MatcherResult Evaluate(Control control)
    {
        var prop = control.GetType().GetProperty(PropertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (prop is null)
            return MatcherResult.Failed($"{control.GetType().Name} has no property '{PropertyName}'");

        var raw = prop.GetValue(control);
        var actual = raw?.ToString() ?? "";
        return actual == Expected
            ? MatcherResult.Passed($"{PropertyName}=\"{actual}\"")
            : MatcherResult.Failed($"expected {PropertyName}=\"{Expected}\", got \"{actual}\"");
    }

    public override string Describe() => $"have property {PropertyName}=\"{Expected}\"";
}
