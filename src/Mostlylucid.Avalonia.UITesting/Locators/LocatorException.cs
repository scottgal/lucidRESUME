namespace Mostlylucid.Avalonia.UITesting.Locators;

/// <summary>Base exception for locator failures.</summary>
public class LocatorException : Exception
{
    public string? Selector { get; }

    public LocatorException(string message, string? selector = null, Exception? inner = null)
        : base(message, inner)
    {
        Selector = selector;
    }
}

/// <summary>Thrown when a locator does not resolve any controls within the timeout.</summary>
public sealed class LocatorTimeoutException : LocatorException
{
    public int TimeoutMs { get; }

    public LocatorTimeoutException(string selector, int timeoutMs, Exception? inner = null)
        : base($"Locator '{selector}' did not match any control within {timeoutMs}ms", selector, inner)
    {
        TimeoutMs = timeoutMs;
    }
}

/// <summary>Thrown when a locator matches more than one control where exactly one was required.</summary>
public sealed class LocatorAmbiguousException : LocatorException
{
    public int MatchCount { get; }

    public LocatorAmbiguousException(string selector, int matchCount)
        : base($"Locator '{selector}' matched {matchCount} controls; use first(), last(), or nth(N, ...) to disambiguate", selector)
    {
        MatchCount = matchCount;
    }
}

/// <summary>Thrown when a selector string cannot be parsed.</summary>
public sealed class SelectorParseException : LocatorException
{
    public int Position { get; }

    public SelectorParseException(string selector, int position, string reason)
        : base($"Selector parse error at position {position}: {reason} (in '{selector}')", selector)
    {
        Position = position;
    }
}
