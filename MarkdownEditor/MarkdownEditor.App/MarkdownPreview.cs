using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdDocument = Markdig.Syntax.MarkdownDocument;

namespace MarkdownEditor.App;

public sealed class MarkdownPreview(StackPanel host, Func<string, Task> openLink) : IDisposable
{
    private readonly List<Bitmap> bitmaps = [];
    private readonly Dictionary<string, Control> anchors = new(StringComparer.OrdinalIgnoreCase);
    private string? sourcePath;
    private double availableWidth = 360;
    private static readonly IBrush Green = Brush.Parse("#177452");
    private static readonly IBrush Muted = Brush.Parse("#68717C");

    public void Render(MdDocument document, string? path, double width)
    {
        host.Children.Clear();
        Dispose();
        anchors.Clear();
        sourcePath = path;
        availableWidth = Math.Max(160, width - 52);
        foreach (var block in document) host.Children.Add(RenderBlock(block));
    }

    public bool ScrollToAnchor(string anchor)
    {
        if (!anchors.TryGetValue(Uri.UnescapeDataString(anchor.TrimStart('#')), out var control)) return false;
        control.BringIntoView();
        return true;
    }

    private Control RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = InlineText(heading.Inline);
                title.FontSize = heading.Level switch { 1 => 28, 2 => 22, 3 => 18, _ => 16 };
                title.LineHeight = title.FontSize * 1.4;
                title.FontWeight = FontWeight.SemiBold;
                title.Margin = new Thickness(0, 8, 0, 2);
                if (heading.GetAttributes().Id is { } id) anchors[id] = title;
                return title;
            case ParagraphBlock paragraph:
                if (paragraph.Inline?.FirstChild is LinkInline { IsImage: true, NextSibling: null } imageLink)
                    return Image(imageLink);
                return InlineText(paragraph.Inline);
            case CodeBlock code:
                return new Border
                {
                    Background = Brush.Parse("#F1F3F5"), Padding = new Thickness(14), CornerRadius = new CornerRadius(4),
                    Child = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new SelectableTextBlock { Text = code.Lines.ToString(), FontFamily = new FontFamily("Consolas, monospace"), FontSize = 13 }
                    }
                };
            case QuoteBlock quote:
                return new Border { BorderBrush = Green, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(14, 3), Child = Blocks(quote) };
            case ListBlock list:
                var panel = new StackPanel { Spacing = 6 };
                var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
                    var task = ((ContainerBlock)item).FirstOrDefault() is ParagraphBlock first
                        ? first.Inline?.OfType<TaskList>().FirstOrDefault() : null;
                    if (task is not null)
                        row.Children.Add(new CheckBox
                        {
                            IsChecked = task.Checked, IsHitTestVisible = false, Focusable = false,
                            MinWidth = 0, MinHeight = 0, Width = 24, Height = 32, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, -4, 4, 0)
                        });
                    else
                        row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", Foreground = Muted, Margin = new Thickness(0, 2, 4, 0) });
                    var contents = Blocks((ContainerBlock)item);
                    Grid.SetColumn(contents, 1);
                    row.Children.Add(contents);
                    panel.Children.Add(row);
                }
                return panel;
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = Brush.Parse("#DCE1E5"), Margin = new Thickness(0, 8) };
            case Table table: return RenderTable(table);
            case ContainerBlock container: return Blocks(container);
            case LeafBlock leaf: return InlineText(leaf.Inline);
            default: return new TextBlock();
        }
    }

    private StackPanel Blocks(ContainerBlock container)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in container) panel.Children.Add(RenderBlock(child));
        return panel;
    }

    private Control RenderTable(Table table)
    {
        var grid = new Grid();
        var columnCount = table.OfType<TableRow>().Select(row => row.Count).DefaultIfEmpty(1).Max();
        for (var col = 0; col < columnCount; col++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var rowIndex = 0;
        foreach (TableRow row in table)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var col = 0;
            foreach (TableCell cell in row)
            {
                var content = Blocks(cell);
                if (row.IsHeader) TextElement.SetFontWeight(content, FontWeight.SemiBold);
                var border = new Border
                {
                    BorderBrush = Brush.Parse("#DCE1E5"), BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = Brush.Parse(row.IsHeader ? "#F1F3F5" : "#FFFFFF"), Padding = new Thickness(10, 8), Child = content,
                    MinWidth = 70, MaxWidth = 260
                };
                Grid.SetColumn(border, col++); Grid.SetRow(border, rowIndex);
                grid.Children.Add(border);
            }
            rowIndex++;
        }
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = grid
        };
    }

    private SelectableTextBlock InlineText(ContainerInline? inline)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineSpacing = 4 };
        if (inline is not null) Fill(block.Inlines!, inline);
        return block;
    }

    private void Fill(InlineCollection output, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal: output.Add(new Run(literal.Content.ToString())); break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard) output.Add(new LineBreak());
                    else output.Add(new Run(" "));
                    break;
                case CodeInline code:
                    output.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas, monospace"), Foreground = Green }); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        span.FontWeight = FontWeight.Bold;
                        if (emphasis.DelimiterCount == 3) span.FontStyle = FontStyle.Italic;
                    }
                    else span.FontStyle = FontStyle.Italic;
                    Fill(span.Inlines, emphasis);
                    output.Add(span); break;
                case LinkInline link when link.IsImage:
                    output.Add(new InlineUIContainer(Image(link))); break;
                case LinkInline link:
                    var button = new Button
                    {
                        Content = InlineText(link), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        Padding = new Thickness(0), MinHeight = 0, Foreground = Green,
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                    };
                    ToolTip.SetTip(button, link.Url);
                    button.Click += async (_, _) => { if (link.Url is { } url) await openLink(url); };
                    output.Add(new InlineUIContainer(button)); break;
                case AutolinkInline auto:
                    var autoButton = new Button { Content = auto.Url, Foreground = Green, Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0 };
                    autoButton.Click += async (_, _) => await openLink(auto.IsEmail ? "mailto:" + auto.Url : auto.Url);
                    output.Add(new InlineUIContainer(autoButton)); break;
                case TaskList task:
                    // The containing list row renders the checkbox outside the text baseline.
                    break;
                case HtmlEntityInline entity: output.Add(new Run(entity.Transcoded.ToString())); break;
                case ContainerInline nested: Fill(output, nested); break;
            }
        }
    }

    private Control Image(LinkInline link)
    {
        var label = string.Concat(link.Descendants<LiteralInline>().Select(x => x.Content.ToString()));
        var url = link.Url ?? "";
        try
        {
            Uri? uri = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) uri = absolute;
            else if (sourcePath is not null) uri = new Uri(new Uri(sourcePath), url);
            if (uri?.IsFile == true && File.Exists(uri.LocalPath))
            {
                if (new FileInfo(uri.LocalPath).Length > 16 * 1024 * 1024) throw new IOException("Изображение больше 16 МБ");
                using var stream = File.OpenRead(uri.LocalPath);
                var bitmap = Bitmap.DecodeToWidth(stream, 1200);
                bitmaps.Add(bitmap);
                return new Image { Source = bitmap, MaxWidth = availableWidth, MaxHeight = 400, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { }
        var fallback = new Button
        {
            Content = new TextBlock { Text = string.IsNullOrEmpty(label) ? "Изображение" : label, TextWrapping = TextWrapping.Wrap },
            MaxWidth = availableWidth, Foreground = Green, Padding = new Thickness(8)
        };
        ToolTip.SetTip(fallback, url);
        fallback.Click += async (_, _) => await openLink(url);
        return fallback;
    }

    public void Dispose() { foreach (var bitmap in bitmaps) bitmap.Dispose(); bitmaps.Clear(); }
}
