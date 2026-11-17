using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Thesis.Schema;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownRow = Markdig.Extensions.Tables.TableRow;

namespace Thesis.Core;

/// <summary>Shared, loss-aware adapters for the native document model. No desktop dependencies.</summary>
public static class ContentEditing
{
    public static List<ThesisContentBlock> Read(List<string> paragraphs, List<ThesisContentBlock> blocks, List<ThesisTableContent> tables)
    {
        var result = blocks.Count > 0 ? blocks : paragraphs.Select(text => new ThesisContentBlock { Text = text }).ToList();
        if (blocks.Count == 0) result = [.. result, .. tables.Select(table => new ThesisContentBlock { Type = "table", Table = table })];
        return ThesisJson.Deserialize<List<ThesisContentBlock>>(ThesisJson.Serialize(result));
    }

    public static string Text(ThesisContentBlock block) => block.Runs.Count > 0
        ? string.Concat(block.Runs.Select(run => run.Text)) : block.Text ?? block.Latex ?? block.Caption ?? "";

    public static ThesisContentBlock CreateTable(int rows, int columns)
    {
        if (rows is < 1 or > 100 || columns is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(rows), "表格支持 1–100 行、1–20 列。");
        return new() { Type = "table", Table = new() { Headers = Enumerable.Repeat("", columns).ToList(),
            Rows = Enumerable.Range(0, rows).Select(_ => Enumerable.Repeat("", columns).ToList()).ToList() } };
    }

    public static void ResizeTable(ThesisTableContent table, int rows, int columns)
    {
        _ = CreateTable(rows, columns);
        static void Resize(List<string> values, int count) { while (values.Count < count) values.Add(""); if (values.Count > count) values.RemoveRange(count, values.Count - count); }
        Resize(table.Headers, columns);
        while (table.Rows.Count < rows) table.Rows.Add([]);
        if (table.Rows.Count > rows) table.Rows.RemoveRange(rows, table.Rows.Count - rows);
        foreach (var row in table.Rows) Resize(row, columns);
    }

    public static bool TryParseMarkdown(string markdown, out List<ThesisContentBlock> blocks)
    {
        var result = new List<ThesisContentBlock>();
        bool ReadContainer(ContainerBlock container, string? list = null, int depth = 0)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case Markdig.Extensions.Mathematics.MathBlock math:
                        result.Add(new() { Type = "equation", Latex = math.Lines.ToString(), DisplayEquation = true }); break;
                    case MarkdownTable table:
                        var rows = table.OfType<MarkdownRow>().Select(row => row.Select(cell => string.Join(" ", ((ContainerBlock)cell).OfType<LeafBlock>().Select(leaf => MarkdownSupport.PlainText(leaf.Inline)))).ToList()).ToList();
                        // Rich cells remain in the interchange form rather than silently losing their formatting.
                        if (table.Descendants<EmphasisInline>().Any() || table.Descendants<LinkInline>().Any()) return false;
                        result.Add(new() { Type = "table", Table = new() { Headers = rows.FirstOrDefault() ?? [], Rows = rows.Skip(1).ToList() } }); break;
                    case FencedCodeBlock fence when fence.Info is "thesis-block" or "thesis-native":
                        result.Add(ThesisJson.DeserializeStrict<ThesisContentBlock>(fence.Lines.ToString())); break;
                    case ListBlock items:
                        if (items.IsOrdered && items.OrderedStart != "1") return false;
                        foreach (var item in items.OfType<ListItemBlock>()) if (!ReadContainer(item, items.IsOrdered ? "numbered" : "bullet", depth + 1)) return false;
                        break;
                    case LeafBlock leaf when leaf is ParagraphBlock or HeadingBlock:
                        if (leaf is HeadingBlock { Level: > 3 }) return false;
                        var runs = new List<ThesisTextRun>();
                        var images = new List<ThesisContentBlock>();
                        if (!ReadInline(leaf.Inline, new(), runs, images)) return false;
                        if (runs.Count > 0 || images.Count == 0) result.Add(new() { Text = string.Concat(runs.Select(r => r.Text)), Runs = runs,
                            ParagraphStyle = leaf is HeadingBlock heading ? "heading" + heading.Level : "body", ListKind = list, ListLevel = list is null ? null : depth - 1 });
                        // Interleaved inline images need inline positioning; retain their source until that is supported.
                        if (runs.Count > 0 && images.Count > 0) return false;
                        result.AddRange(images); break;
                    default: return false;
                }
            }
            return true;
        }
        try
        {
            if (!ReadContainer(MarkdownSupport.Parse(markdown))) { blocks = []; return false; }
            blocks = result; return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { blocks = []; return false; }
    }

    private static bool ReadInline(ContainerInline? inline, ThesisTextRun format, List<ThesisTextRun> runs, List<ThesisContentBlock> images)
    {
        if (inline is null) return true;
        foreach (var item in inline)
        {
            var next = new ThesisTextRun { Bold = format.Bold, Italic = format.Italic, Strike = format.Strike,
                Code = format.Code, Superscript = format.Superscript, Subscript = format.Subscript };
            switch (item)
            {
                case EmphasisInline emphasis:
                    if (emphasis.DelimiterChar is '*' or '_') { next.Bold |= emphasis.DelimiterCount >= 2; next.Italic |= emphasis.DelimiterCount % 2 == 1; }
                    else if (emphasis.DelimiterChar == '~') { next.Strike |= emphasis.DelimiterCount >= 2; next.Subscript |= emphasis.DelimiterCount == 1; }
                    else if (emphasis.DelimiterChar == '^') next.Superscript = true;
                    else return false;
                    if (!ReadInline(emphasis, next, runs, images)) return false;
                    continue;
                case LinkInline { IsImage: true } image:
                    images.Add(new() { Type = "image", Path = image.Url, Caption = MarkdownSupport.PlainText(image), AltText = MarkdownSupport.PlainText(image) }); continue;
                case LiteralInline literal: next.Text = literal.Content.ToString(); break;
                case CodeInline code: next.Text = code.Content; next.Code = true; break;
                case LineBreakInline lineBreak: next.Text = lineBreak.IsHard ? "\v" : " "; break;
                default: return false;
            }
            runs.Add(next);
        }
        return true;
    }
}
