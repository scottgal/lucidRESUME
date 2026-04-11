using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Expect;

/// <summary>
/// Fluent builder for Playwright-style expectations:
/// <code>
///     await Expect(page.ByName("Status")).ToHaveText("Saved");
///     await Expect("name=SaveBtn").ToBeEnabled();
///     await Expect(By.Type&lt;ListBoxItem&gt;()).Not.ToHaveCount(0);
/// </code>
///
/// Each terminal method (<c>ToHaveText</c>, <c>ToBeEnabled</c>, ...) constructs an
/// <see cref="Expectation"/> and returns its <see cref="Expectation.AssertAsync"/>
/// task. The caller awaits it; on failure the task surfaces an
/// <see cref="ExpectTimeoutException"/> with the locator + matcher diagnostics.
/// </summary>
public sealed class ExpectBuilder
{
    private readonly Locator _locator;
    private readonly Func<Expectation, Task> _runner;
    private readonly int? _timeoutMs;
    private readonly bool _negate;

    public ExpectBuilder(Locator locator, Func<Expectation, Task> runner, int? timeoutMs = null, bool negate = false)
    {
        _locator = locator;
        _runner = runner;
        _timeoutMs = timeoutMs;
        _negate = negate;
    }

    /// <summary>Override the timeout for this expectation only.</summary>
    public ExpectBuilder WithTimeout(int timeoutMs)
        => new(_locator, _runner, timeoutMs, _negate);

    /// <summary>Inverts the expectation — equivalent to Playwright's <c>expect(...).not</c>.</summary>
    public ExpectBuilder Not => new(_locator, _runner, _timeoutMs, !_negate);

    public Task ToBeVisible() => Run(new IsVisibleMatcher());
    public Task ToBeHidden() => Run(new IsHiddenMatcher());
    public Task ToBeEnabled() => Run(new IsEnabledMatcher());
    public Task ToBeDisabled() => Run(new IsDisabledMatcher());
    public Task ToBeChecked() => Run(new IsCheckedMatcher());
    public Task ToBeUnchecked() => Run(new IsUncheckedMatcher());
    public Task ToBeFocused() => Run(new IsFocusedMatcher());
    public Task ToHaveText(string expected) => Run(new HasTextMatcher(expected, exact: true));
    public Task ToContainText(string expected) => Run(new ContainsTextMatcher(expected));
    public Task ToMatchRegex(string pattern) => Run(new MatchesRegexMatcher(pattern));
    public Task ToHaveCount(int expected) => Run(new HasCountMatcher(expected));
    public Task ToHaveValue(string expected) => Run(new HasValueMatcher(expected));
    public Task ToHaveProperty(string name, string expected) => Run(new HasPropertyMatcher(name, expected));

    private Task Run(Matcher matcher)
    {
        var actualMatcher = _negate ? matcher.Not() : matcher;
        var expectation = _timeoutMs is int t
            ? new Expectation(_locator, actualMatcher) { TimeoutMs = t }
            : new Expectation(_locator, actualMatcher);
        return _runner(expectation);
    }
}
