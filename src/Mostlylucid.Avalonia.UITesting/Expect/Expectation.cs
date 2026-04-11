using Avalonia.Controls;
using Avalonia.Threading;
using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Expect;

/// <summary>
/// Pairs a <see cref="Locator"/> with a <see cref="Matcher"/> and polls until
/// the matcher passes against the resolved control or a timeout elapses. This
/// is the core of Playwright-style web-first assertions: tests no longer need
/// to insert <c>Wait</c> actions before checking state.
/// </summary>
public sealed class Expectation
{
    public Locator Locator { get; }
    public Matcher Matcher { get; }
    public int TimeoutMs { get; init; } = 5000;
    public int PollIntervalMs { get; init; } = 50;

    public Expectation(Locator locator, Matcher matcher)
    {
        Locator = locator;
        Matcher = matcher;
    }

    /// <summary>
    /// Resolve the locator and evaluate the matcher repeatedly until it passes
    /// or the timeout elapses. Throws <see cref="ExpectTimeoutException"/> on
    /// timeout, including the last failure detail and the locator/matcher
    /// describes for diagnostics.
    /// </summary>
    public async Task AssertAsync(Control root, LocatorEngine? engine = null)
    {
        engine ??= new LocatorEngine();
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
        string? lastDetail = null;
        Exception? lastError = null;

        while (true)
        {
            try
            {
                // Resolve fresh each iteration so a control that appears late is found.
                // We deliberately give the locator a tiny inner timeout so polling stays
                // responsive — the outer loop drives the real timeout budget.
                var control = await engine.ResolveFirstAsync(Locator, root, timeoutMs: PollIntervalMs);
                var result = await Dispatcher.UIThread.InvokeAsync(() => Matcher.Evaluate(control));
                if (result.Pass) return;
                lastDetail = result.Detail;
            }
            catch (LocatorTimeoutException ex)
            {
                lastDetail = ex.Message;
                lastError = ex;
            }

            if (DateTime.UtcNow >= deadline)
                throw new ExpectTimeoutException(Locator.Describe(), Matcher.Describe(), TimeoutMs, lastDetail, lastError);

            await Task.Delay(PollIntervalMs);
        }
    }
}

/// <summary>Thrown when an expectation does not hold within its timeout.</summary>
public sealed class ExpectTimeoutException : Exception
{
    public string LocatorDescription { get; }
    public string MatcherDescription { get; }
    public int TimeoutMs { get; }
    public string? LastDetail { get; }

    public ExpectTimeoutException(string locator, string matcher, int timeoutMs, string? lastDetail, Exception? inner)
        : base(BuildMessage(locator, matcher, timeoutMs, lastDetail), inner)
    {
        LocatorDescription = locator;
        MatcherDescription = matcher;
        TimeoutMs = timeoutMs;
        LastDetail = lastDetail;
    }

    private static string BuildMessage(string locator, string matcher, int timeoutMs, string? lastDetail)
    {
        var msg = $"Expectation failed after {timeoutMs}ms — expected '{locator}' to {matcher}";
        if (!string.IsNullOrEmpty(lastDetail))
            msg += $"\n  Last seen: {lastDetail}";
        return msg;
    }
}
