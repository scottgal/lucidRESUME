using Avalonia.Controls;

namespace Mostlylucid.Avalonia.UITesting.Expect;

/// <summary>
/// A matcher evaluates a single condition against a control. Matchers are
/// pure (no side effects) so they can be polled repeatedly by the
/// <see cref="Expectation"/> orchestrator until they pass or timeout.
/// </summary>
public abstract class Matcher
{
    /// <summary>
    /// Run the matcher against <paramref name="control"/>. Must be called on the
    /// UI thread.
    /// </summary>
    public abstract MatcherResult Evaluate(Control control);

    /// <summary>Short human-readable description for diagnostics and failure messages.</summary>
    public abstract string Describe();

    /// <summary>Wrap this matcher to require the opposite outcome.</summary>
    public Matcher Not() => new NotMatcher(this);

    public override string ToString() => Describe();
}

/// <summary>Result of a single matcher evaluation.</summary>
public readonly record struct MatcherResult(bool Pass, string Detail)
{
    public static MatcherResult Passed(string detail = "ok") => new(true, detail);
    public static MatcherResult Failed(string detail) => new(false, detail);
}

/// <summary>Inverts another matcher's outcome (the Playwright <c>not.</c> idiom).</summary>
internal sealed class NotMatcher : Matcher
{
    private readonly Matcher _inner;
    public NotMatcher(Matcher inner) { _inner = inner; }

    public override MatcherResult Evaluate(Control control)
    {
        var result = _inner.Evaluate(control);
        return result.Pass
            ? MatcherResult.Failed($"expected NOT {_inner.Describe()}, but it held: {result.Detail}")
            : MatcherResult.Passed($"NOT {_inner.Describe()}");
    }

    public override string Describe() => $"not({_inner.Describe()})";
}
