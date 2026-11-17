using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Thesis.Core;

namespace Thesis.Gui;

internal static class RichMarkdownEditor
{
    private sealed record Span(int Start, int Length, bool Bold, bool Italic, bool Strike, bool Code = false);
    private sealed record Paragraph(int Start, int Length, ParagraphStyle Style, MarkerType List, int Depth);
    private sealed class DocumentBuilder
    {
        public readonly System.Text.StringBuilder Text = new();
        public readonly List<Span> Spans = [];
        public readonly List<Paragraph> Paragraphs = [];

        public void AddParagraph(ContainerInline? inline, ParagraphStyle style = ParagraphStyle.Normal, MarkerType list = MarkerType.None, int depth = 0)
        {
            var start = Text.Length;
            AddInline(inline, false, false, false);
            Paragraphs.Add(new(start, Text.Length - start, style, list, depth));
            Text.Append('\n');
        }

        private void AddInline(ContainerInline? inline, bool bold, bool italic, bool strike, bool code = false)
        {
            if (inline is null) return;
            foreach (var item in inline)
            {
                switch (item)
                {
                    case LiteralInline literal:
                        AddText(literal.Content.ToString(), bold, italic, strike, code);
                        break;
                    case CodeInline codeInline:
                        AddText(codeInline.Content, bold, italic, strike, true);
                        break;
                    case EmphasisInline emphasis:
                        AddInline(emphasis, bold || emphasis.DelimiterChar == '*' && emphasis.DelimiterCount >= 2,
                            italic || emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 1,
                            strike || emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2, code);
                        break;
                    case LinkInline:
                    case AutolinkInline:
                        throw new InvalidDataException("本章包含超链接。请用 Markdown 源码视图编辑，以保留完整链接地址。");
                    case LineBreakInline:
                        AddText("\n", bold, italic, strike, code);
                        break;
                    default:
                        throw new InvalidDataException("本章包含无法在富文本视图中保真编辑的内容。");
                }
            }
        }

        private void AddText(string text, bool bold, bool italic, bool strike, bool code)
        {
            if (text.Length == 0) return;
            var start = Text.Length;
            Text.Append(text);
            if (bold || italic || strike || code) Spans.Add(new(start, text.Length, bold, italic, strike, code));
        }
    }

    public static bool Supports(string markdown, out string reason)
    {
        try { _ = Build(markdown); reason = ""; return true; }
        catch (InvalidDataException ex) { reason = ex.Message; return false; }
    }

    public static void Load(RichEditBox editor, string markdown)
    {
        var model = Build(markdown);
        var document = editor.Document;
        document.SetText(TextSetOptions.None, model.Text.ToString());
        var characterFormat = document.GetDefaultCharacterFormat();
        characterFormat.Name = "Microsoft YaHei UI";
        characterFormat.Size = 12;
        document.SetDefaultCharacterFormat(characterFormat);
        foreach (var paragraph in model.Paragraphs)
        {
            if (paragraph.Length == 0) continue;
            var range = document.GetRange(paragraph.Start, paragraph.Start + paragraph.Length);
            range.ParagraphFormat.Style = paragraph.Style;
            if (paragraph.List != MarkerType.None)
            {
                range.ParagraphFormat.ListType = paragraph.List;
                range.ParagraphFormat.SetIndents(24 * (paragraph.Depth + 1), 0, 0);
            }
        }
        foreach (var span in model.Spans)
        {
            var range = document.GetRange(span.Start, span.Start + span.Length);
            if (span.Bold) range.CharacterFormat.Bold = FormatEffect.On;
            if (span.Italic) range.CharacterFormat.Italic = FormatEffect.On;
            if (span.Strike) range.CharacterFormat.Strikethrough = FormatEffect.On;
            if (span.Code) range.CharacterFormat.Name = "Consolas";
        }
        document.Selection.SetRange(0, 0);
    }

    public static string ReadMarkdown(RichEditBox editor)
    {
        editor.Document.GetText(TextGetOptions.UseLf, out var text);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\0', '\n');
        if (text.Length == 0) return "";

        var result = new List<string>();
        var start = 0;
        foreach (var line in text.Split('\n'))
        {
            var range = editor.Document.GetRange(start, start + line.Length);
            var style = range.ParagraphFormat.Style;
            var list = range.ParagraphFormat.ListType;
            var body = ReadInline(editor, line, start);
            body = style switch
            {
                ParagraphStyle.Heading1 => "# " + body,
                ParagraphStyle.Heading2 => "## " + body,
                ParagraphStyle.Heading3 => "### " + body,
                _ => body
            };
            if (list is MarkerType.Bullet or MarkerType.BlackCircleWingding or MarkerType.WhiteCircleWingding)
                body = "- " + body;
            else if (list is not MarkerType.None and not MarkerType.Undefined)
                body = "1. " + body;
            result.Add(body);
            start += line.Length + 1;
        }
        return string.Join("\n\n", result).TrimEnd();
    }

    private static string ReadInline(RichEditBox editor, string text, int offset)
    {
        var runs = new List<string>();
        ReadRuns(editor, text, offset, 0, text.Length, runs);
        return string.Concat(runs);
    }

    private static void ReadRuns(RichEditBox editor, string text, int offset, int start, int length, List<string> output)
    {
        if (length == 0) return;
        var range = editor.Document.GetRange(offset + start, offset + start + length);
        var format = range.CharacterFormat;
        var mixed = format.Bold is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Italic is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Strikethrough is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Superscript is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Subscript is FormatEffect.Undefined or FormatEffect.Toggle;
        if (mixed && length > 1)
        {
            var half = length / 2;
            if (char.IsHighSurrogate(text[start + half - 1]) && char.IsLowSurrogate(text[start + half])) half--;
            ReadRuns(editor, text, offset, start, half, output);
            ReadRuns(editor, text, offset, start + half, length - half, output);
            return;
        }

        var value = text.Substring(start, length);
        var code = string.Equals(format.Name, "Consolas", StringComparison.OrdinalIgnoreCase);
        if (code) value = "`" + value.Replace("`", "\\`") + "`";
        else value = Escape(value);
        if (format.Superscript == FormatEffect.On) value = "^" + value + "^";
        if (format.Subscript == FormatEffect.On) value = "~" + value + "~";
        if (format.Strikethrough == FormatEffect.On) value = "~~" + value + "~~";
        if (format.Italic == FormatEffect.On) value = "*" + value + "*";
        if (format.Bold == FormatEffect.On) value = "**" + value + "**";
        output.Add(value);
    }

    private static string Escape(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\\' or '*' or '_' or '~' or '^' or '[' or ']') result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }

    private static DocumentBuilder Build(string markdown)
    {
        var model = new DocumentBuilder();
        var document = MarkdownSupport.Parse(markdown);
        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    model.AddParagraph(heading.Inline, heading.Level switch
                    {
                        1 => ParagraphStyle.Heading1,
                        2 => ParagraphStyle.Heading2,
                        _ => ParagraphStyle.Heading3
                    });
                    break;
                case ParagraphBlock paragraph:
                    model.AddParagraph(paragraph.Inline);
                    break;
                case ListBlock list:
                    AddList(model, list, 0);
                    break;
                default:
                    throw new InvalidDataException("本章包含表格、公式、图片或原稿保真片段。请用 Markdown 源码视图编辑，以完整保留这些内容。");
            }
        }
        if (model.Text.Length > 0) model.Text.Length--;
        return model;
    }

    private static void AddList(DocumentBuilder model, ListBlock list, int depth)
    {
        foreach (var item in list.OfType<ListItemBlock>())
        foreach (var block in item)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    model.AddParagraph(paragraph.Inline, list: list.IsOrdered ? MarkerType.Arabic : MarkerType.Bullet, depth: depth);
                    break;
                case ListBlock nested:
                    AddList(model, nested, depth + 1);
                    break;
                default:
                    throw new InvalidDataException("本章包含无法在富文本视图中保真编辑的嵌套列表内容。");
            }
        }
    }
}
