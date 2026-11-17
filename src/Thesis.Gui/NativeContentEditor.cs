using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Gui;

internal static class NativeContentEditor
{
    private sealed record InlineFormat(bool Bold, bool Italic, bool Strike, bool Superscript, bool Subscript, bool Code, bool Underline);
    private sealed record InlineRun(string Text, InlineFormat Format);

    public static bool SupportsBlocks(IReadOnlyList<ThesisContentBlock> blocks) => blocks.All(block => block.Type == "paragraph");

    public static void LoadBlocks(RichEditBox editor, IReadOnlyList<ThesisContentBlock> blocks)
    {
        if (!SupportsBlocks(blocks)) throw new InvalidDataException("此编辑器仅载入段落；表格、图片、公式和原稿对象由对应的原生对象控件编辑。");
        var text = new System.Text.StringBuilder();
        var ranges = new List<(ThesisContentBlock Block, int Start, int Length)>();
        foreach (var block in blocks)
        {
            var content = ContentEditing.Text(block);
            var start = text.Length;
            text.Append(content);
            ranges.Add((block, start, content.Length));
            text.Append('\n');
        }
        if (text.Length > 0) text.Length--;

        var document = editor.Document;
        document.SetText(TextSetOptions.None, text.ToString());
        var defaultFormat = document.GetDefaultCharacterFormat();
        defaultFormat.Name = "Microsoft YaHei UI";
        defaultFormat.Size = 12;
        document.SetDefaultCharacterFormat(defaultFormat);
        foreach (var (block, start, length) in ranges)
        {
            var range = document.GetRange(start, start + length);
            range.ParagraphFormat.Style = block.ParagraphStyle?.ToLowerInvariant() switch
            {
                "heading1" => ParagraphStyle.Heading1,
                "heading2" => ParagraphStyle.Heading2,
                "heading3" => ParagraphStyle.Heading3,
                _ => ParagraphStyle.Normal
            };
            range.ParagraphFormat.ListType = block.ListKind switch
            {
                "bullet" => MarkerType.Bullet,
                "numbered" => MarkerType.Arabic,
                _ => MarkerType.None
            };
            if (block.ListKind is not null) range.ParagraphFormat.SetIndents(24 * ((block.ListLevel ?? 0) + 1), 0, 0);
            var offset = start;
            foreach (var run in block.Runs)
            {
                if (run.Text.Length == 0) continue;
                var runRange = document.GetRange(offset, offset + run.Text.Length);
                if (run.Bold) runRange.CharacterFormat.Bold = FormatEffect.On;
                if (run.Italic) runRange.CharacterFormat.Italic = FormatEffect.On;
                if (run.Underline) runRange.CharacterFormat.Underline = UnderlineType.Single;
                if (run.Strike) runRange.CharacterFormat.Strikethrough = FormatEffect.On;
                if (run.Superscript) runRange.CharacterFormat.Superscript = FormatEffect.On;
                if (run.Subscript) runRange.CharacterFormat.Subscript = FormatEffect.On;
                if (run.Code) runRange.CharacterFormat.Name = "Consolas";
                offset += run.Text.Length;
            }
        }
        document.Selection.SetRange(0, 0);
        document.ClearUndoRedoHistory();
    }

    public static List<ThesisContentBlock> ReadBlocks(RichEditBox editor)
    {
        editor.Document.GetText(TextGetOptions.UseLf, out var text);
        text = NormalizeDocumentText(text);
        if (text.Length == 0) return [];
        var blocks = new List<ThesisContentBlock>();
        var start = 0;
        foreach (var line in text.Split('\n'))
        {
            var range = editor.Document.GetRange(start, start + line.Length);
            var style = range.ParagraphFormat.Style switch
            {
                ParagraphStyle.Heading1 => "heading1",
                ParagraphStyle.Heading2 => "heading2",
                ParagraphStyle.Heading3 => "heading3",
                _ => "body"
            };
            var listKind = range.ParagraphFormat.ListType switch
            {
                MarkerType.Bullet or MarkerType.BlackCircleWingding or MarkerType.WhiteCircleWingding => "bullet",
                MarkerType.None or MarkerType.Undefined => null,
                _ => "numbered"
            };
            blocks.Add(new ThesisContentBlock
            {
                Text = line, ParagraphStyle = style, ListKind = listKind,
                ListLevel = listKind is null ? null : Math.Max(0, (int)Math.Round(range.ParagraphFormat.LeftIndent / 24) - 1),
                Runs = ReadStructuredRuns(editor, line, start)
            });
            start += line.Length + 1;
        }
        return blocks;
    }

    private static string NormalizeDocumentText(string value)
    {
        value = value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\0');
        return value.EndsWith('\n') ? value[..^1] : value;
    }

    private static List<ThesisTextRun> ReadStructuredRuns(RichEditBox editor, string text, int offset)
    {
        var runs = new List<InlineRun>();
        ReadRuns(editor, text, offset, 0, text.Length, runs);
        var result = new List<ThesisTextRun>();
        foreach (var run in runs)
        {
            if (result.Count > 0 && result[^1] is { } previous
                && previous.Bold == run.Format.Bold && previous.Italic == run.Format.Italic
                && previous.Strike == run.Format.Strike && previous.Code == run.Format.Code
                && previous.Underline == run.Format.Underline
                && previous.Superscript == run.Format.Superscript && previous.Subscript == run.Format.Subscript)
                result[^1].Text += run.Text;
            else result.Add(new ThesisTextRun
            {
                Text = run.Text, Bold = run.Format.Bold, Italic = run.Format.Italic,
                Strike = run.Format.Strike, Code = run.Format.Code, Underline = run.Format.Underline,
                Superscript = run.Format.Superscript, Subscript = run.Format.Subscript
            });
        }
        return result;
    }

    private static void ReadRuns(RichEditBox editor, string text, int offset, int start, int length, List<InlineRun> output)
    {
        if (length == 0) return;
        var format = editor.Document.GetRange(offset + start, offset + start + length).CharacterFormat;
        var mixed = format.Bold is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Italic is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Strikethrough is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Superscript is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Subscript is FormatEffect.Undefined or FormatEffect.Toggle
            || format.Underline == UnderlineType.Undefined || string.IsNullOrEmpty(format.Name);
        if (mixed && length > 1)
        {
            var half = length / 2;
            if (char.IsHighSurrogate(text[start + half - 1]) && char.IsLowSurrogate(text[start + half])) half--;
            if (half == 0) half = 2;
            if (half < length)
            {
                ReadRuns(editor, text, offset, start, half, output);
                ReadRuns(editor, text, offset, start + half, length - half, output);
                return;
            }
        }
        output.Add(new(text.Substring(start, length), new(format.Bold == FormatEffect.On,
            format.Italic == FormatEffect.On, format.Strikethrough == FormatEffect.On,
            format.Superscript == FormatEffect.On, format.Subscript == FormatEffect.On,
            string.Equals(format.Name, "Consolas", StringComparison.OrdinalIgnoreCase),
            format.Underline is not (UnderlineType.None or UnderlineType.Undefined))));
    }
}
