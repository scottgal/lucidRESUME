using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using lucidRESUME.JobML;

namespace lucidRESUME.Web;

public sealed class MarkdigResumeMarkdownRenderer : IResumeMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    public string ToTranscriptHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var paragraphs = document.Descendants<ParagraphBlock>().ToList();
        foreach (var passage in MarkdownEvidenceIndex.Create(markdown).Passages)
        {
            var paragraph = paragraphs.FirstOrDefault(block =>
                block.Span.Start <= passage.SourceStart && block.Span.End >= passage.SourceStart);
            if (paragraph is null) continue;
            paragraph.GetAttributes().Id ??= passage.Reference.TrimStart('#');
        }
        return Markdown.ToHtml(document, Pipeline);
    }
}
