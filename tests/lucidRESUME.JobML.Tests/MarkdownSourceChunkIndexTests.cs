using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class MarkdownSourceChunkIndexTests
{
    [Fact]
    public void ReassemblesTheFullTranscriptExactly()
    {
        var source = "# Career transcript\r\n\r\n" +
            string.Concat(Enumerable.Repeat("A role paragraph with an emoji 😀 and evidence.\r\n\r\n", 80)) +
            "```text\r\nverbatim source\r\n```\r\n";
        var index = MarkdownSourceChunkIndex.Create(source, 160);

        Assert.True(index.Chunks.Count > 1);
        Assert.Equal(source, index.Reassemble());
        Assert.Equal(source.Length, index.Chunks.Sum(chunk => chunk.SourceLength));
        Assert.All(index.Chunks, chunk => Assert.DoesNotContain('\uFFFD', chunk.Text));
    }

    [Fact]
    public void EmptyTranscriptHasAStableEmptyManifest()
    {
        var index = MarkdownSourceChunkIndex.Create("");
        Assert.Empty(index.Chunks);
        Assert.Equal("", index.Reassemble());
    }

    [Fact]
    public void DoesNotSplitASurrogatePairAcrossChunks()
    {
        var source = new string('a', 127) + "😀" + new string('b', 130);
        var index = MarkdownSourceChunkIndex.Create(source, 128);

        Assert.Equal(source, index.Reassemble());
        Assert.All(index.Chunks, chunk =>
            Assert.False(chunk.Text.Length > 0 && char.IsHighSurrogate(chunk.Text[^1])));
    }
}
