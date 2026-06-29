using Avalonia.Input;
using Mostlylucid.Avalonia.UITesting.Players;

namespace Mostlylucid.Avalonia.UITesting.Tests;

/// <summary>
/// Locks down ScriptPlayer.ParseKeyChord. The whole point of the chord
/// parser is letting YAML scripts express ordinary shortcuts (Ctrl+L,
/// Ctrl+Shift+P) without having to wire individual KeyDown events with
/// modifier state in code. Every commonly-used modifier alias is covered
/// because the manual-capture YAML in downstream apps uses several.
/// </summary>
public class KeyChordParserTests
{
    [Fact]
    public void Parse_PlainKey_ReturnsNoModifiers()
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord("Enter");
        Assert.Equal(Key.Enter, key);
        Assert.Equal(KeyModifiers.None, mods);
    }

    [Fact]
    public void Parse_FunctionKey_ReturnsCorrectEnum()
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord("F11");
        Assert.Equal(Key.F11, key);
        Assert.Equal(KeyModifiers.None, mods);
    }

    [Theory]
    [InlineData("Ctrl+L", Key.L, KeyModifiers.Control)]
    [InlineData("Control+L", Key.L, KeyModifiers.Control)]
    [InlineData("Shift+Tab", Key.Tab, KeyModifiers.Shift)]
    [InlineData("Alt+F4", Key.F4, KeyModifiers.Alt)]
    [InlineData("Option+F4", Key.F4, KeyModifiers.Alt)]
    [InlineData("Meta+C", Key.C, KeyModifiers.Meta)]
    [InlineData("Cmd+C", Key.C, KeyModifiers.Meta)]
    [InlineData("Command+C", Key.C, KeyModifiers.Meta)]
    [InlineData("Win+E", Key.E, KeyModifiers.Meta)]
    public void Parse_SingleModifier_ParsesAllAliases(string spec, Key expectedKey, KeyModifiers expectedMods)
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord(spec);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedMods, mods);
    }

    [Fact]
    public void Parse_MultipleModifiers_OrsThemTogether()
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord("Ctrl+Shift+P");
        Assert.Equal(Key.P, key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, mods);
    }

    [Fact]
    public void Parse_AllFourModifiers_OrsThemTogether()
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord("Ctrl+Shift+Alt+Meta+A");
        Assert.Equal(Key.A, key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, mods);
    }

    [Fact]
    public void Parse_ModifierOrderIndependent()
    {
        var (key1, mods1) = ScriptPlayer.ParseKeyChord("Ctrl+Shift+P");
        var (key2, mods2) = ScriptPlayer.ParseKeyChord("Shift+Ctrl+P");
        Assert.Equal(key1, key2);
        Assert.Equal(mods1, mods2);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var (key1, mods1) = ScriptPlayer.ParseKeyChord("ctrl+l");
        var (key2, mods2) = ScriptPlayer.ParseKeyChord("CTRL+L");
        var (key3, mods3) = ScriptPlayer.ParseKeyChord("Ctrl+L");
        Assert.Equal(key1, key2);
        Assert.Equal(key1, key3);
        Assert.Equal(mods1, mods2);
        Assert.Equal(mods1, mods3);
    }

    [Fact]
    public void Parse_SurroundingWhitespace_Trimmed()
    {
        var (key, mods) = ScriptPlayer.ParseKeyChord(" Ctrl + L ");
        Assert.Equal(Key.L, key);
        Assert.Equal(KeyModifiers.Control, mods);
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptPlayer.ParseKeyChord(""));
    }

    [Fact]
    public void Parse_WhitespaceOnly_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptPlayer.ParseKeyChord("   "));
    }

    [Fact]
    public void Parse_UnknownKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScriptPlayer.ParseKeyChord("Ctrl+Bogus"));
        Assert.Contains("Bogus", ex.Message);
    }

    [Fact]
    public void Parse_UnknownModifier_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScriptPlayer.ParseKeyChord("Hyper+L"));
        Assert.Contains("Hyper", ex.Message);
    }

    [Fact]
    public void Parse_NoKey_OnlyModifiers_Throws()
    {
        // "Ctrl+" — last token would be empty/missing. Split removes empties
        // so this collapses to just "Ctrl" and we treat that as a key lookup
        // (which fails — there's no Avalonia.Input.Key.Ctrl).
        Assert.Throws<InvalidOperationException>(() => ScriptPlayer.ParseKeyChord("Ctrl+"));
    }
}