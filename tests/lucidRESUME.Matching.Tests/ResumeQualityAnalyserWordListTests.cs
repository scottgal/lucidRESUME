using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class ResumeQualityAnalyserWordListTests
{
    [Fact]
    public void LoadWordList_WhenResourceExists_LoadsEntriesFromResourcesFolder()
    {
        var words = ResumeQualityAnalyser.LoadWordList("strong-verbs.txt", ["fallback-only"]).Value;

        Assert.Contains("architected", words);
        Assert.Contains("optimized", words);
        Assert.DoesNotContain("fallback-only", words);
    }

    [Fact]
    public void LoadWordList_WhenResourceMissing_UsesFallbackWords()
    {
        var words = ResumeQualityAnalyser.LoadWordList(
            $"missing-{Guid.NewGuid():N}.txt",
            ["fallback-a", "fallback-b"]).Value;

        Assert.Contains("fallback-a", words);
        Assert.Contains("fallback-b", words);
    }
}
