using Avalonia.Controls;

namespace Mostlylucid.Avalonia.UITesting.Locators;

/// <summary>
/// A recipe for finding controls in a window. Locators are evaluated lazily — they
/// resolve at action time and can be retried until they match or timeout. This is
/// the foundation for Playwright-style auto-waiting interactions.
///
/// Locators are <em>composable</em>: every locator can produce a new locator via
/// <see cref="First"/>, <see cref="Last"/>, <see cref="Nth"/>, <see cref="Filter"/>,
/// <see cref="Inside"/>, <see cref="Near"/>, and <see cref="HasText"/>.
/// </summary>
public abstract class Locator
{
    /// <summary>
    /// The original selector string this locator was parsed from, if any. Used for
    /// error messages and tracing.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Synchronously enumerate all controls under <paramref name="root"/> that match
    /// this locator. Must be called on the UI thread (the locator engine handles
    /// dispatcher marshalling).
    /// </summary>
    public abstract IEnumerable<Control> Resolve(Control root);

    /// <summary>Pick only the first match.</summary>
    public Locator First() => new IndexLocator(this, 0) { Source = $"first({Describe()})" };

    /// <summary>Pick only the last match.</summary>
    public Locator Last() => new LastLocator(this) { Source = $"last({Describe()})" };

    /// <summary>Pick only the Nth match (zero-based).</summary>
    public Locator Nth(int index) => new IndexLocator(this, index) { Source = $"nth({index},{Describe()})" };

    /// <summary>Filter results by an arbitrary predicate.</summary>
    public Locator Filter(Func<Control, bool> predicate, string? describe = null)
        => new FilterLocator(this, predicate, describe) { Source = $"{Describe()}.filter({describe ?? "<predicate>"})" };

    /// <summary>Filter to controls whose visual subtree contains <paramref name="text"/>.</summary>
    public Locator HasText(string text, bool exact = false)
        => new HasTextLocator(this, text, exact) { Source = $"{Describe()}:has-text({text})" };

    /// <summary>Match only controls that are descendants of <paramref name="container"/>.</summary>
    public Locator Inside(Locator container)
        => new InsideLocator(this, container) { Source = $"inside({container.Describe()}) {Describe()}" };

    /// <summary>
    /// Reorder matches by spatial proximity to <paramref name="anchor"/>'s center,
    /// closest first. Useful for "the button next to the email field".
    /// </summary>
    public Locator Near(Locator anchor)
        => new NearLocator(this, anchor) { Source = $"near({anchor.Describe()}) {Describe()}" };

    /// <summary>Best-effort string description for diagnostics.</summary>
    public virtual string Describe() => Source ?? GetType().Name;

    public override string ToString() => Describe();
}
