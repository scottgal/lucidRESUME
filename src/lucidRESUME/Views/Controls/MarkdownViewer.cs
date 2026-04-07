using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace lucidRESUME.Views.Controls;

/// <summary>
/// Renders Markdown content as native Avalonia controls.
/// Supports headings, paragraphs, lists, code blocks, bold, italic, code spans, links.
/// Hidden HTML comments <!-- help:tag --> are parsed as help anchors for contextual help.
/// </summary>
public sealed partial class MarkdownViewer : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(Markdown));

    public static readonly StyledProperty<string?> ScrollToAnchorProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(ScrollToAnchor));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? ScrollToAnchor
    {
        get => GetValue(ScrollToAnchorProperty);
        set => SetValue(ScrollToAnchorProperty, value);
    }

    private readonly StackPanel _container = new() { Spacing = 8 };
    private readonly ScrollViewer _scrollViewer;
    private readonly Dictionary<string, Control> _anchors = new(StringComparer.OrdinalIgnoreCase);

    public MarkdownViewer()
    {
        _scrollViewer = new ScrollViewer
        {
            Content = _container,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Content = _scrollViewer;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty)
            Render();
        else if (change.Property == ScrollToAnchorProperty)
            ScrollTo(change.GetNewValue<string?>());
    }

    public void ScrollTo(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return;
        if (_anchors.TryGetValue(anchor, out var control))
        {
            // Scroll the control into view
            control.BringIntoView();
        }
    }

    private void Render()
    {
        _container.Children.Clear();
        _anchors.Clear();

        var md = Markdown;
        if (string.IsNullOrWhiteSpace(md)) return;

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var doc = Markdig.Markdown.Parse(md, pipeline);

        string? pendingAnchor = null;

        foreach (var block in doc)
        {
            // Check for help anchors in HTML comments preceding blocks
            var anchor = ExtractHelpAnchor(block, md);
            if (anchor != null) pendingAnchor = anchor;

            var control = RenderBlock(block, md);
            if (control == null) continue;

            if (pendingAnchor != null)
            {
                _anchors[pendingAnchor] = control;
                pendingAnchor = null;
            }

            // Also register heading text as anchors
            if (block is HeadingBlock heading)
            {
                var headingText = GetInlineText(heading.Inline);
                if (!string.IsNullOrEmpty(headingText))
                {
                    var slug = Slugify(headingText);
                    _anchors.TryAdd(slug, control);
                    _anchors.TryAdd(headingText, control);
                }
            }

            _container.Children.Add(control);
        }
    }

    private Control? RenderBlock(Block block, string source)
    {
        return block switch
        {
            HeadingBlock h => RenderHeading(h),
            ParagraphBlock p => RenderParagraph(p),
            ListBlock l => RenderList(l, source),
            FencedCodeBlock f => RenderCodeBlock(f),
            CodeBlock c => RenderCodeBlock(c),
            ThematicBreakBlock => RenderHr(),
            QuoteBlock q => RenderQuote(q, source),
            Table table => RenderTable(table, source),
            HtmlBlock html => RenderHtmlBlock(html, source),
            _ => null
        };
    }

    private Control RenderHeading(HeadingBlock heading)
    {
        var (fontSize, weight) = heading.Level switch
        {
            1 => (26d, FontWeight.Bold),
            2 => (21d, FontWeight.Bold),
            3 => (17d, FontWeight.SemiBold),
            4 => (15d, FontWeight.SemiBold),
            _ => (14d, FontWeight.Medium)
        };

        var tb = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("LrPrimaryTextBrush"),
            Margin = heading.Level <= 2 ? new Thickness(0, 12, 0, 4) : new Thickness(0, 8, 0, 2)
        };

        AppendInlines(tb, heading.Inline);

        if (heading.Level <= 2)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(tb);
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = Brush("LrBorderBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            return panel;
        }

        return tb;
    }

    private Control RenderParagraph(ParagraphBlock para)
    {
        var tb = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("LrSecondaryTextBrush"),
            LineHeight = 22,
            Margin = new Thickness(0, 2, 0, 2)
        };
        AppendInlines(tb, para.Inline);
        return tb;
    }

    private Control RenderList(ListBlock list, string source)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(16, 4, 0, 4) };
        int index = 1;

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;

            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var bullet = list.IsOrdered
                ? new TextBlock { Text = $"{index++}.", FontSize = 14, Foreground = Brush("LrAccentBlueBrush"), VerticalAlignment = VerticalAlignment.Top, MinWidth = 20 }
                : new TextBlock { Text = "\u2022", FontSize = 14, Foreground = Brush("LrAccentBlueBrush"), VerticalAlignment = VerticalAlignment.Top, MinWidth = 14 };

            itemPanel.Children.Add(bullet);

            var contentPanel = new StackPanel { Spacing = 2 };
            foreach (var subBlock in listItem)
            {
                var ctrl = RenderBlock(subBlock, source);
                if (ctrl != null)
                    contentPanel.Children.Add(ctrl);
            }
            itemPanel.Children.Add(contentPanel);
            panel.Children.Add(itemPanel);
        }

        return panel;
    }

    private Control RenderTable(Table table, string source)
    {
        // Count columns
        int colCount = 0;
        foreach (var block in table)
        {
            if (block is TableRow row)
            {
                colCount = Math.Max(colCount, row.Count);
                break;
            }
        }
        if (colCount == 0) return new Border();

        // Build Grid with column definitions
        var colDefs = string.Join(",", Enumerable.Repeat("*", colCount));
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(colDefs) };

        int rowIndex = 0;
        foreach (var block in table)
        {
            if (block is not TableRow row) continue;

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            bool isHeader = row.IsHeader;

            for (int col = 0; col < row.Count && col < colCount; col++)
            {
                if (row[col] is not TableCell cell) continue;

                var cellText = new TextBlock
                {
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = isHeader ? Brush("LrPrimaryTextBrush") : Brush("LrSecondaryTextBrush"),
                    FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
                    Margin = new Thickness(0)
                };

                // Extract text from cell paragraphs
                foreach (var cellBlock in cell)
                {
                    if (cellBlock is ParagraphBlock p)
                        AppendInlines(cellText, p.Inline);
                }

                var cellBorder = new Border
                {
                    BorderBrush = Brush("LrBorderBrush"),
                    BorderThickness = new Thickness(0, 0, col < colCount - 1 ? 1 : 0, 1),
                    Padding = new Thickness(8, 6),
                    Background = isHeader ? new SolidColorBrush(Color.FromArgb(20, 137, 180, 250)) : null,
                    Child = cellText
                };

                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, col);
                grid.Children.Add(cellBorder);
            }

            rowIndex++;
        }

        return new Border
        {
            BorderBrush = Brush("LrBorderBrush"),
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 4),
            ClipToBounds = true,
            Child = grid
        };
    }

    private static Control RenderCodeBlock(LeafBlock code)
    {
        var text = code.Lines.ToString().TrimEnd();
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Child = new SelectableTextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#A6E3A1"))
            }
        };
    }

    private Control RenderHr()
    {
        return new Border
        {
            Height = 1,
            Background = Brush("LrBorderBrush"),
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private Control RenderQuote(QuoteBlock quote, string source)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var child in quote)
        {
            var ctrl = RenderBlock(child, source);
            if (ctrl != null) panel.Children.Add(ctrl);
        }

        return new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brush("LrAccentBlueBrush"),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(15, 137, 180, 250)),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Child = panel
        };
    }

    private Control? RenderHtmlBlock(HtmlBlock html, string source)
    {
        // HTML comments (help anchors) are invisible - just register anchors
        var text = html.Lines.ToString();
        var match = HelpAnchorRx().Match(text);
        if (match.Success)
        {
            // Create invisible anchor point
            var anchor = new Border { Height = 0, Tag = match.Groups[1].Value };
            _anchors[match.Groups[1].Value] = anchor;
            return anchor;
        }
        return null;
    }

    private void AppendInlines(TextBlock textBlock, ContainerInline? container)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    textBlock.Inlines!.Add(new Run(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    var tempTb = new TextBlock();
                    AppendInlines(tempTb, emphasis);
                    var text = string.Concat(tempTb.Inlines!.OfType<Run>().Select(r => r.Text));
                    var allText = GetInlineText(emphasis);

                    if (emphasis.DelimiterCount == 2) // bold
                    {
                        textBlock.Inlines!.Add(new Run(allText) { FontWeight = FontWeight.Bold });
                    }
                    else if (emphasis.DelimiterCount == 1) // italic
                    {
                        textBlock.Inlines!.Add(new Run(allText) { FontStyle = FontStyle.Italic });
                    }
                    else if (emphasis.DelimiterCount >= 3) // bold italic
                    {
                        textBlock.Inlines!.Add(new Run(allText) { FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic });
                    }
                    break;

                case CodeInline code:
                    textBlock.Inlines!.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
                        Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                        Foreground = new SolidColorBrush(Color.Parse("#F38BA8"))
                    });
                    break;

                case LinkInline link:
                    var linkText = GetInlineText(link);
                    textBlock.Inlines!.Add(new Run(linkText)
                    {
                        Foreground = Brush("LrAccentBlueBrush"),
                        TextDecorations = TextDecorations.Underline
                    });
                    break;

                case LineBreakInline:
                    textBlock.Inlines!.Add(new LineBreak());
                    break;

                case HtmlInline html:
                    // Skip HTML tags (including help comments in inline context)
                    break;
            }
        }
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container == null) return "";
        var parts = new List<string>();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    parts.Add(lit.Content.ToString());
                    break;
                case EmphasisInline em:
                    parts.Add(GetInlineText(em));
                    break;
                case CodeInline code:
                    parts.Add(code.Content);
                    break;
                case LinkInline link:
                    parts.Add(GetInlineText(link));
                    break;
            }
        }
        return string.Join("", parts);
    }

    private static string? ExtractHelpAnchor(Block block, string source)
    {
        // Look at the raw source for <!-- help:xxx --> comments before this block
        if (block.Line <= 0) return null;
        var lines = source.Split('\n');
        if (block.Line >= lines.Length) return null;

        // Check the line before this block
        var prevLine = block.Line > 0 ? lines[block.Line - 1].Trim() : "";
        var match = HelpAnchorRx().Match(prevLine);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Slugify(string text)
    {
        return SlugRx().Replace(text.ToLowerInvariant().Replace(' ', '-'), "");
    }

    private static ISolidColorBrush Brush(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var val) == true
            && val is ISolidColorBrush brush)
            return brush;
        return Brushes.Gray;
    }

    [GeneratedRegex(@"<!--\s*help:(\S+)\s*-->")]
    private static partial Regex HelpAnchorRx();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugRx();
}