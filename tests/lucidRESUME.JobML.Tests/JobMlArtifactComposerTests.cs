using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class JobMlArtifactComposerTests
{
    [Fact]
    public void Compose_AddsLabelArticleAndSingleJobMlBlock()
    {
        var generated = JobMlDraftGenerator.Generate("# Jane\n\n## Experience\n\nBuilt a reliable platform for customers.");

        var artifact = JobMlArtifactComposer.Compose(generated);

        Assert.Contains("## MACHINE AREA", artifact);
        Assert.Contains(JobMlArtifactComposer.ArticleUrl, artifact);
        Assert.Equal(1, Count(artifact, "```jobml"));
        Assert.True(new JobMlParser().TryParse(artifact, out _, out var error), error);

        var reparsed = new JobMlParser().Parse(artifact);
        Assert.DoesNotContain("MACHINE AREA", reparsed.Markdown);
        Assert.Equal(generated.Markdown.Trim(), reparsed.Markdown.Trim());
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }
}
