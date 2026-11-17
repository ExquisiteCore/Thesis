using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Thesis.Schema;

namespace Thesis.Core;

public static class MarkdownSupport
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseMathematics().DisableHtml().Build();
    public static MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, Pipeline);

    public static string PlainText(ContainerInline? inline) => inline is null ? "" : string.Concat(inline.Select(item => item switch
    {
        LiteralInline literal => literal.Content.ToString(),
        CodeInline code => code.Content,
        Markdig.Extensions.Mathematics.MathInline math => "$" + math.Content.ToString() + "$",
        LineBreakInline => "\n",
        ContainerInline container => PlainText(container),
        _ => item.ToString()
    }));

    public static IEnumerable<string> AssetPaths(string markdown, string directory)
    {
        foreach (var link in Parse(markdown).Descendants<LinkInline>().Where(link => link.IsImage))
            if (LocalPath(link.Url, directory) is { } path) yield return path;
        foreach (var fence in Parse(markdown).Descendants<FencedCodeBlock>().Where(block => block.Info == "thesis-native"))
        {
            var native = ThesisJson.DeserializeStrict<ThesisContentBlock>(fence.Lines.ToString());
            if (native.SourcePath is not null) yield return Path.GetFullPath(native.SourcePath, directory);
        }
    }

    public static string? LocalPath(string? url, string directory)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Path.IsPathFullyQualified(url)) return Path.GetFullPath(url);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.IsFile ? uri.LocalPath : null;
        return Path.GetFullPath(Uri.UnescapeDataString(url), directory);
    }

    public static string RewriteAssets(string markdown, string directory, Func<string, string> import)
    {
        var replacements = new List<(int Start, int Length, string Text)>();
        var document = Parse(markdown);
        foreach (var link in document.Descendants<LinkInline>().Where(link => link.IsImage))
        {
            if (LocalPath(link.Url, directory) is not { } path) continue;
            var target = import(path).Replace('\\', '/').Replace(" ", "%20");
            replacements.Add((link.Span.Start, link.Span.Length, $"![{PlainText(link).Replace("]", "\\]")}](<{target}>)"));
        }
        foreach (var fence in document.Descendants<FencedCodeBlock>().Where(block => block.Info == "thesis-native"))
        {
            var native = ThesisJson.DeserializeStrict<ThesisContentBlock>(fence.Lines.ToString());
            if (native.SourcePath is null) throw new InvalidDataException("原生片段缺少源文件。");
            native.SourcePath = import(Path.GetFullPath(native.SourcePath, directory));
            replacements.Add((fence.Span.Start, fence.Span.Length, NativeFence(native)));
        }
        var builder = new StringBuilder(markdown);
        foreach (var item in replacements.OrderByDescending(item => item.Start)) builder.Remove(item.Start, item.Length).Insert(item.Start, item.Text);
        return builder.ToString();
    }

    public static string NativeFence(ThesisContentBlock block) => "```thesis-native\n" + ThesisJson.Serialize(block) + "\n```";
    public static string Body(List<string> paragraphs, List<ThesisContentBlock> blocks, List<ThesisTableContent> tables)
    {
        var result = new List<string>();
        if (blocks.Count == 0) result.AddRange(paragraphs);
        foreach (var block in blocks)
            result.Add(block.Type switch
            {
                "native" => NativeFence(block),
                "image" => $"![{block.Caption ?? block.AltText}](<{block.Path?.Replace('\\', '/')}>)",
                "table" when block.Table is not null => Table(block.Table),
                _ => block.Text ?? ""
            });
        if (blocks.Count == 0) result.AddRange(tables.Select(Table));
        return string.Join("\n\n", result);
    }

    public static string Table(ThesisTableContent table)
    {
        var count = Math.Max(table.Headers.Count, table.Rows.Select(row => row.Count).DefaultIfEmpty(0).Max());
        string Row(IEnumerable<string> values) => "| " + string.Join(" | ", values.Select(value => value.Replace("|", "\\|").Replace("\n", " "))) + " |";
        var headers = table.Headers.Count > 0 ? table.Headers : Enumerable.Repeat("", count).ToList();
        return (table.Caption is null ? "" : table.Caption + "\n\n") + Row(headers) + "\n" + Row(Enumerable.Repeat("---", count)) + "\n" + string.Join("\n", table.Rows.Select(Row));
    }
}
