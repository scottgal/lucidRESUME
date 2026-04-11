namespace Mostlylucid.Avalonia.UITesting.Locators;

/// <summary>
/// Static factory for building <see cref="Locator"/> instances programmatically.
/// Mirrors the string DSL exposed by <see cref="SelectorParser"/>.
///
/// <code>
///     var save = By.Name("SaveBtn");
///     var firstButton = By.Type("Button").First();
///     var emailInput = By.Label("Email");
///     var nearJobList = By.Type("Button").Near(By.Name("JobList"));
/// </code>
/// </summary>
public static class By
{
    public static Locator Name(string name) => new NameLocator(name);
    public static Locator Type(string typeName) => new TypeLocator(typeName);
    public static Locator Type<T>() => new TypeLocator(typeof(T).Name);
    public static Locator Text(string text, bool exact = false) => new TextLocator(text, exact);
    public static Locator Role(string role, string? accessibleName = null) => new RoleLocator(role, accessibleName);
    public static Locator TestId(string testId) => new TestIdLocator(testId);
    public static Locator Label(string label) => new LabelLocator(label);

    /// <summary>Parse a selector string into a locator.</summary>
    public static Locator Selector(string selector) => SelectorParser.Parse(selector);
}
