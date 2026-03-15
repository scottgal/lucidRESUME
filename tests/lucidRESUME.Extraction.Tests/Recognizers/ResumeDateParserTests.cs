using lucidRESUME.Extraction.Recognizers;

namespace lucidRESUME.Extraction.Tests.Recognizers;

public class ResumeDateParserTests
{
    // ── ContainsDate ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Senior Developer Jan 2020 - Present", true)]
    [InlineData("ZenChef Limited | Lead Contract Developer | Remote Oct 2024 -Present", true)]
    [InlineData("University of Stirling | BSc Psychology 2008 - 2011", true)]
    [InlineData("Some random line about skills", false)]
    [InlineData("", false)]
    public void ContainsDate_DetectsDatePresence(string text, bool expected)
    {
        Assert.Equal(expected, ResumeDateParser.ContainsDate(text));
    }

    // ── ExtractFirstDateRange ─────────────────────────────────────────────────

    [Fact]
    public void ExtractFirstDateRange_StandardRange_ReturnsStartAndEnd()
    {
        var result = ResumeDateParser.ExtractFirstDateRange("Worked at Acme from Jan 2019 to Dec 2022");
        Assert.NotNull(result);
        Assert.Equal(2019, result.Start?.Year);
        Assert.Equal(2022, result.End?.Year);
        Assert.False(result.IsCurrent);
    }

    [Theory]
    [InlineData("Jan 2020 – Present")]
    [InlineData("Jan 2020 - Present")]
    [InlineData("January 2020 to present")]
    [InlineData("2020 - now")]
    [InlineData("2020 – now")]
    [InlineData("2020 to date")]
    [InlineData("Oct 2024 - Present")]
    public void ExtractFirstDateRange_OpenEndedRange_IsCurrentTrue(string text)
    {
        var result = ResumeDateParser.ExtractFirstDateRange(text);
        Assert.NotNull(result);
        Assert.True(result.IsCurrent, $"Expected IsCurrent=true for: {text}");
    }

    [Fact]
    public void ExtractFirstDateRange_YearOnlyRange_ReturnsYears()
    {
        var result = ResumeDateParser.ExtractFirstDateRange("2019 – 2022");
        Assert.NotNull(result);
        Assert.Equal(2019, result.Start?.Year);
        Assert.Equal(2022, result.End?.Year);
        Assert.False(result.IsCurrent);
    }

    [Fact]
    public void ExtractFirstDateRange_FullHeadingWithPresent_IsCurrentTrue()
    {
        // Exact text from MarkdownSectionParserTests sample
        var result = ResumeDateParser.ExtractFirstDateRange(
            "ZenChef Limited | Lead Contract Developer | Remote Oct 2024 -Present");
        Assert.NotNull(result);
        Assert.Equal(2024, result.Start?.Year);
        Assert.True(result.IsCurrent);
    }

    [Fact]
    public void ExtractFirstDateRange_NoDate_ReturnsNull()
    {
        var result = ResumeDateParser.ExtractFirstDateRange("C# Python JavaScript SQL");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractFirstDateRange_EmptyText_ReturnsNull()
    {
        Assert.Null(ResumeDateParser.ExtractFirstDateRange(""));
        Assert.Null(ResumeDateParser.ExtractFirstDateRange(null!));
    }

    [Fact]
    public void ExtractFirstDateRange_DatePrefixedJobLine_StartsNearZero()
    {
        // Pattern 3: Eastern European CV format
        var text = "2020 - now:  Java Developer, Bank (Tel Aviv)";
        var result = ResumeDateParser.ExtractFirstDateRange(text);
        Assert.NotNull(result);
        Assert.Equal(2020, result.Start?.Year);
        Assert.True(result.IsCurrent);
        Assert.True(result.MatchStart <= 2, $"Expected match to start at/near 0, was {result.MatchStart}");
    }

    [Fact]
    public void ExtractFirstDateRange_MatchOffsets_AreWithinText()
    {
        var text = "Senior Developer Jan 2020 - Dec 2023";
        var result = ResumeDateParser.ExtractFirstDateRange(text);
        Assert.NotNull(result);
        Assert.True(result.MatchStart >= 0);
        Assert.True(result.MatchEnd < text.Length);
        Assert.True(result.MatchEnd >= result.MatchStart);
    }
}
