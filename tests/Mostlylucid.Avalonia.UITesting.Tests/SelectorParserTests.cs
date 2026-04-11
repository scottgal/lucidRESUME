using Mostlylucid.Avalonia.UITesting.Locators;

namespace Mostlylucid.Avalonia.UITesting.Tests;

public class SelectorParserTests
{
    [Fact]
    public void BareWord_BackwardsCompat_ParsesAsName()
    {
        var locator = SelectorParser.Parse("SaveBtn");
        Assert.IsType<NameLocator>(locator);
        Assert.Equal("SaveBtn", ((NameLocator)locator).Name);
    }

    [Fact]
    public void NameAtom_Parses()
    {
        var locator = SelectorParser.Parse("name=SaveBtn");
        var name = Assert.IsType<NameLocator>(locator);
        Assert.Equal("SaveBtn", name.Name);
    }

    [Fact]
    public void TypeAtom_Parses()
    {
        var locator = SelectorParser.Parse("type=Button");
        var t = Assert.IsType<TypeLocator>(locator);
        Assert.Equal("Button", t.TypeName);
    }

    [Fact]
    public void TextAtom_Substring_Default()
    {
        var locator = SelectorParser.Parse("text=Save");
        var text = Assert.IsType<TextLocator>(locator);
        Assert.Equal("Save", text.Text);
        Assert.False(text.Exact);
    }

    [Fact]
    public void TextAtom_QuotedIsExact()
    {
        var locator = SelectorParser.Parse("text='Save Resume'");
        var text = Assert.IsType<TextLocator>(locator);
        Assert.Equal("Save Resume", text.Text);
        Assert.True(text.Exact);
    }

    [Fact]
    public void TextAtom_DoubleQuotedIsExact()
    {
        var locator = SelectorParser.Parse("text=\"Save Resume\"");
        var text = Assert.IsType<TextLocator>(locator);
        Assert.Equal("Save Resume", text.Text);
        Assert.True(text.Exact);
    }

    [Fact]
    public void TestIdAtom_Parses()
    {
        var locator = SelectorParser.Parse("testid=save-btn");
        var id = Assert.IsType<TestIdLocator>(locator);
        Assert.Equal("save-btn", id.TestId);
    }

    [Fact]
    public void RoleAtom_Parses()
    {
        var locator = SelectorParser.Parse("role=button");
        var role = Assert.IsType<RoleLocator>(locator);
        Assert.Equal("button", role.Role);
        Assert.Null(role.AccessibleName);
    }

    [Fact]
    public void LabelAtom_Parses()
    {
        var locator = SelectorParser.Parse("label=Email");
        var label = Assert.IsType<LabelLocator>(locator);
        Assert.Equal("Email", label.LabelText);
    }

    [Fact]
    public void First_Function_Parses()
    {
        var locator = SelectorParser.Parse("first(type=Button)");
        var idx = Assert.IsType<IndexLocator>(locator);
        Assert.Equal(0, idx.Index);
        Assert.IsType<TypeLocator>(idx.Inner);
    }

    [Fact]
    public void Last_Function_Parses()
    {
        var locator = SelectorParser.Parse("last(type=Button)");
        var last = Assert.IsType<LastLocator>(locator);
        Assert.IsType<TypeLocator>(last.Inner);
    }

    [Fact]
    public void Nth_Function_Parses()
    {
        var locator = SelectorParser.Parse("nth(2, type=ListBoxItem)");
        var idx = Assert.IsType<IndexLocator>(locator);
        Assert.Equal(2, idx.Index);
        Assert.Equal("ListBoxItem", ((TypeLocator)idx.Inner).TypeName);
    }

    [Fact]
    public void Inside_ComposesContainerAndInner()
    {
        var locator = SelectorParser.Parse("inside(name=Header) type=TextBlock");
        var inside = Assert.IsType<InsideLocator>(locator);
        Assert.IsType<NameLocator>(inside.Container);
        Assert.IsType<TypeLocator>(inside.Inner);
    }

    [Fact]
    public void Near_ComposesAnchorAndInner()
    {
        var locator = SelectorParser.Parse("near(name=JobList) type=Button");
        var near = Assert.IsType<NearLocator>(locator);
        Assert.IsType<NameLocator>(near.Anchor);
        Assert.IsType<TypeLocator>(near.Inner);
    }

    [Fact]
    public void MultipleAtoms_ComposeWithImplicitAnd()
    {
        // type=Button text=Save → type=Button filtered by text=Save
        var locator = SelectorParser.Parse("type=Button text=Save");
        Assert.IsType<FilterLocator>(locator);
    }

    [Fact]
    public void HasTextPseudo_OnAtom_Parses()
    {
        var locator = SelectorParser.Parse("type=Button:has-text(Save)");
        var has = Assert.IsType<HasTextLocator>(locator);
        Assert.Equal("Save", has.Text);
        Assert.False(has.Exact);
    }

    [Fact]
    public void HasTextPseudo_QuotedExact_Parses()
    {
        var locator = SelectorParser.Parse("type=Button:has-text('Save Resume')");
        var has = Assert.IsType<HasTextLocator>(locator);
        Assert.Equal("Save Resume", has.Text);
        Assert.True(has.Exact);
    }

    [Fact]
    public void Empty_Throws()
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse(""));
    }

    [Fact]
    public void UnknownKey_Throws()
    {
        var ex = Assert.Throws<SelectorParseException>(() => SelectorParser.Parse("nope=value"));
        Assert.Contains("unknown key", ex.Message);
    }

    [Fact]
    public void MissingEquals_Throws()
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse("name SaveBtn"));
    }

    [Fact]
    public void UnterminatedQuote_Throws()
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse("text='Save"));
    }

    [Fact]
    public void UnknownFunction_Throws()
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse("foo(name=Save)"));
    }

    [Fact]
    public void UnknownPseudo_Throws()
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse("type=Button:nope(value)"));
    }
}
