using Avalonia.Controls;
using Avalonia.Threading;

namespace Mostlylucid.Avalonia.UITesting.Locators;

/// <summary>
/// Resolves a <see cref="Locator"/> against a window with auto-retry until the
/// locator matches or a timeout elapses. This is what makes interactions
/// reliable: scripts don't need to insert <c>Wait</c> actions before clicking a
/// control that's still being created.
/// </summary>
public sealed class LocatorEngine
{
    /// <summary>Default poll interval between retry attempts.</summary>
    public int PollIntervalMs { get; init; } = 50;

    /// <summary>Default total timeout for resolution attempts.</summary>
    public int DefaultTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Resolve a locator to exactly one control, retrying on the UI thread until
    /// the locator matches a single control or the timeout elapses. Throws
    /// <see cref="LocatorTimeoutException"/> if no match appears, or
    /// <see cref="LocatorAmbiguousException"/> if more than one control matches.
    /// </summary>
    public async Task<Control> ResolveOneAsync(Locator locator, Control root, int? timeoutMs = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs ?? DefaultTimeoutMs);
        Exception? lastError = null;

        while (true)
        {
            try
            {
                var matches = await Dispatcher.UIThread.InvokeAsync(() => locator.Resolve(root).ToList());
                if (matches.Count == 1) return matches[0];
                if (matches.Count > 1)
                    throw new LocatorAmbiguousException(locator.Describe(), matches.Count);
                lastError = new LocatorException("no matches yet", locator.Describe());
            }
            catch (LocatorAmbiguousException)
            {
                throw; // permanent — retrying won't reduce the match count
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (DateTime.UtcNow >= deadline)
                throw new LocatorTimeoutException(locator.Describe(), timeoutMs ?? DefaultTimeoutMs, lastError);

            await Task.Delay(PollIntervalMs);
        }
    }

    /// <summary>
    /// Resolve to all matching controls. Returns immediately with whatever the
    /// locator currently sees — does not retry. Use this when you expect zero or
    /// many matches.
    /// </summary>
    public async Task<IReadOnlyList<Control>> ResolveAllAsync(Locator locator, Control root)
    {
        var matches = await Dispatcher.UIThread.InvokeAsync(() => locator.Resolve(root).ToList());
        return matches;
    }

    /// <summary>
    /// Resolve to the first match, retrying until something appears or timeout.
    /// Unlike <see cref="ResolveOneAsync"/> this is OK with multiple matches —
    /// it just picks the first.
    /// </summary>
    public async Task<Control> ResolveFirstAsync(Locator locator, Control root, int? timeoutMs = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs ?? DefaultTimeoutMs);
        Exception? lastError = null;

        while (true)
        {
            try
            {
                var match = await Dispatcher.UIThread.InvokeAsync(() => locator.Resolve(root).FirstOrDefault());
                if (match != null) return match;
                lastError = new LocatorException("no matches yet", locator.Describe());
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (DateTime.UtcNow >= deadline)
                throw new LocatorTimeoutException(locator.Describe(), timeoutMs ?? DefaultTimeoutMs, lastError);

            await Task.Delay(PollIntervalMs);
        }
    }

    /// <summary>
    /// Parse a selector string into a locator and resolve it to one control.
    /// Convenience for callers that work in strings (YAML scripts, MCP, REPL).
    /// </summary>
    public Task<Control> ResolveOneAsync(string selector, Control root, int? timeoutMs = null)
        => ResolveOneAsync(SelectorParser.Parse(selector), root, timeoutMs);

    /// <summary>Parse-and-resolve helper for "get me one or more, don't care which".</summary>
    public Task<Control> ResolveFirstAsync(string selector, Control root, int? timeoutMs = null)
        => ResolveFirstAsync(SelectorParser.Parse(selector), root, timeoutMs);
}
