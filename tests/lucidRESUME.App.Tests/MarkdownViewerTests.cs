using Avalonia.Controls;
using lucidRESUME.Views.Controls;

namespace lucidRESUME.App.Tests;

public sealed class MarkdownViewerTests
{
    [Fact]
    public void Jobml_paragraphs_are_visible_in_the_native_markdown_preview()
    {
        var viewer = new MarkdownViewer { Markdown = "<p id=\"claim-api\">\nMaintained **SQL services** and recovery procedures.\n</p>" };
        var scroll = Assert.IsType<ScrollViewer>(viewer.Content);
        var document = Assert.IsType<StackPanel>(scroll.Content);
        var paragraph = Assert.IsType<StackPanel>(Assert.Single(document.Children));
        var text = Assert.IsType<TextBlock>(Assert.Single(paragraph.Children));
        Assert.Contains("Maintained", string.Concat(text.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)));
    }
}
