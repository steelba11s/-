using System.Net;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownEditor.App;

public static class MarkdownDocument
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras().UseAutoLinks().UseAutoIdentifiers().DisableHtml().Build();

    public static Markdig.Syntax.MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    public static string ExportHtml(string text, string title, string? sourcePath = null)
    {
        var document = Parse(text);
        // Local resources remain relative to the source document, even when exporting elsewhere.
        if (sourcePath is not null)
        {
            var origin = new Uri(Path.GetFullPath(sourcePath));
            foreach (var link in document.Descendants<LinkInline>())
                if (!string.IsNullOrWhiteSpace(link.Url) && !link.Url.StartsWith('#') &&
                    !Uri.TryCreate(link.Url, UriKind.Absolute, out _))
                    link.Url = new Uri(origin, link.Url).AbsoluteUri;
        }
        var body = document.ToHtml(Pipeline);
        return $$"""
            <!doctype html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: file: data:; style-src 'unsafe-inline'">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
            body { max-width: 850px; margin: 40px auto; padding: 0 24px; color: #252b32; background: #fff; font: 17px/1.7 system-ui,sans-serif; overflow-wrap: anywhere; }
            h1,h2,h3 { line-height: 1.3; } a { color: #177452; } img { max-width: 100%; height: auto; }
            pre { padding: 16px; background: #f1f3f5; overflow: auto; } code { font-family: Consolas,monospace; }
            blockquote { margin-left: 0; padding-left: 20px; border-left: 3px solid #177452; color: #58616b; }
            table { border-collapse: collapse; display: block; overflow: auto; } td,th { border: 1px solid #d9dfe3; padding: 8px 12px; }
            hr { border: 0; border-top: 1px solid #d9dfe3; } @media print { body { margin: 0; max-width: none; } }
            </style>
            </head><body>
            {{body}}
            </body></html>
            """;
    }
}
