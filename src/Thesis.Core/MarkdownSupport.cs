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
                "equation" => "$$\n" + (block.Latex ?? block.Text) + "\n$$",
                "paragraph" => Paragraph(block),
                _ => NativeFence(block)
            });
        if (blocks.Count == 0) result.AddRange(tables.Select(Table));
        return string.Join("\n\n", result);
    }

    public static string Paragraph(ThesisContentBlock block)
    {
        // Markdown has no portable underline or nested-list format matching every native run.
        if (block.Runs.Any(r => r.Underline) || block.ListLevel is > 0)
            return "```thesis-block\n" + ThesisJson.Serialize(block) + "\n```";
        string Escape(string value)
        {
            var result = new StringBuilder();
            foreach (var c in value) { if ("\\`*_{}[]<>#|$~^!+-".Contains(c)) result.Append('\\'); result.Append(c); }
            return result.ToString().Replace("\v", "  \n");
        }
        string Run(ThesisTextRun run)
        {
            var value = Escape(run.Text.Trim());
            if (value.Length == 0) return run.Text;
            if (run.Code) { var ticks = new string('`', run.Text.Split('`').Length); value = ticks + " " + run.Text.Trim() + " " + ticks; }
            if (run.Superscript) value = "^" + value + "^";
            if (run.Subscript) value = "~" + value + "~";
            if (run.Strike) value = "~~" + value + "~~";
            if (run.Italic) value = "*" + value + "*";
            if (run.Bold) value = "**" + value + "**";
            return run.Text[..(run.Text.Length - run.Text.TrimStart().Length)] + value + run.Text[(run.Text.TrimEnd().Length)..];
        }
        var text = block.Runs.Count > 0 ? string.Concat(block.Runs.Select(Run)) : Escape(block.Text ?? "");
        var prefix = block.ParagraphStyle switch { "heading1" => "# ", "heading2" => "## ", "heading3" => "### ", _ => "" };
        if (block.ListKind == "bullet") prefix = "- " + prefix;
        if (block.ListKind == "numbered") prefix = "1. " + prefix;
        return prefix + text;
    }

    public static string Table(ThesisTableContent table)
    {
        var count = Math.Max(table.Headers.Count, table.Rows.Select(row => row.Count).DefaultIfEmpty(0).Max());
        string Row(IEnumerable<string> values) => "| " + string.Join(" | ", values.Select(value => value.Replace("|", "\\|").Replace("\n", " "))) + " |";
        var headers = table.Headers.Count > 0 ? table.Headers : Enumerable.Repeat("", count).ToList();
        return (table.Caption is null ? "" : table.Caption + "\n\n") + Row(headers) + "\n" + Row(Enumerable.Repeat("---", count)) + "\n" + string.Join("\n", table.Rows.Select(Row));
    }
}
