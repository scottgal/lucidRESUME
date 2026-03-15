using NUnit.Framework;

namespace lucidRESUME.UXTesting.Tests;

[TestFixture]
public class CommandParsingTests
{
    [Test]
    public void SplitCommand_SimpleCommand_ReturnsSinglePart()
    {
        var result = SplitCommand("help");
        
        Assert.That(result, Is.EqualTo(new[] { "help" }));
    }

    [Test]
    public void SplitCommand_CommandWithArg_ReturnsParts()
    {
        var result = SplitCommand("nav Jobs");
        
        Assert.That(result, Is.EqualTo(new[] { "nav", "Jobs" }));
    }

    [Test]
    public void SplitCommand_CommandWithMultipleArgs_ReturnsParts()
    {
        var result = SplitCommand("type SearchBox hello world");
        
        Assert.That(result, Is.EqualTo(new[] { "type", "SearchBox", "hello", "world" }));
    }

    [Test]
    public void SplitCommand_WithQuotes_KeepsQuotedTextTogether()
    {
        var result = SplitCommand("type SearchBox \"hello world\"");
        
        Assert.That(result, Is.EqualTo(new[] { "type", "SearchBox", "hello world" }));
    }

    [Test]
    public void SplitCommand_WithMultipleQuotedParts_KeepsThemSeparate()
    {
        var result = SplitCommand("cmd \"first arg\" \"second arg\"");
        
        Assert.That(result, Is.EqualTo(new[] { "cmd", "first arg", "second arg" }));
    }

    [Test]
    public void SplitCommand_EmptyString_ReturnsEmpty()
    {
        var result = SplitCommand("");
        
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SplitCommand_OnlySpaces_ReturnsEmpty()
    {
        var result = SplitCommand("   ");
        
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SplitCommand_TrailingSpaces_IgnoresThem()
    {
        var result = SplitCommand("nav Jobs   ");
        
        Assert.That(result, Is.EqualTo(new[] { "nav", "Jobs" }));
    }

    [Test]
    public void SplitCommand_LeadingSpaces_IgnoresThem()
    {
        var result = SplitCommand("   nav Jobs");
        
        Assert.That(result, Is.EqualTo(new[] { "nav", "Jobs" }));
    }

    [Test]
    public void SplitCommand_MultipleSpacesBetweenArgs_TreatedAsOne()
    {
        var result = SplitCommand("nav    Jobs");
        
        Assert.That(result, Is.EqualTo(new[] { "nav", "Jobs" }));
    }

    [Test]
    public void SplitCommand_UnclosedQuote_ConsumesQuoteButNotAddedToOutput()
    {
        var result = SplitCommand("type Box \"unclosed");
        
        Assert.That(result, Is.EqualTo(new[] { "type", "Box", "unclosed" }));
    }

    private static string[] SplitCommand(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = "";
                }
            }
            else
            {
                current += c;
            }
        }
        
        if (current.Length > 0)
            result.Add(current);
        
        return result.ToArray();
    }
}
