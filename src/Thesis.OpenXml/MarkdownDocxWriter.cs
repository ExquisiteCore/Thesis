using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Thesis.Schema;
using Thesis.Core;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownRow = Markdig.Extensions.Tables.TableRow;

namespace Thesis.OpenXml;

internal sealed class MarkdownDocxWriter(MainDocumentPart main, Body body, string directory,
    Func<string, string, Paragraph> paragraph, Action<ThesisContentBlock> appendImage, Action<ThesisTableContent> appendTable,
    Func<ThesisContentBlock, OpenXmlElement> appendNative)
{
    public void Append(string markdown) => Write(MarkdownSupport.Parse(markdown));

    private void Write(ContainerBlock container, string prefix = "")
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case Markdig.Extensions.Mathematics.MathBlock math:
                    paragraph("$$\n" + math.Lines.ToString() + "\n$$", "body");
                    break;
                case MarkdownTable table:
                    var rows = table.OfType<MarkdownRow>().Select(row => row.Select(cell => string.Join(" ", ((ContainerBlock)cell).OfType<LeafBlock>().Select(leaf => MarkdownSupport.PlainText(leaf.Inline)))).ToList()).ToList();
                    appendTable(new() { Headers = rows.FirstOrDefault() ?? [], Rows = rows.Skip(1).ToList() });
                    break;
                case FencedCodeBlock fence when fence.Info == "thesis-native":
                    var native = ThesisJson.DeserializeStrict<ThesisContentBlock>(fence.Lines.ToString());
                    if (native.Type != "native" || native.SourcePath is null) throw new InvalidDataException("原生片段描述无效。");
                    native.SourcePath = Path.GetFullPath(native.SourcePath, directory);
                    body.Append(appendNative(native));
                    break;
                case CodeBlock code:
                    foreach (var line in code.Lines.ToString().Split('\n'))
                    {
                        var codeParagraph = paragraph(line, "body");
                        foreach (var run in codeParagraph.Elements<Run>())
                        {
                            run.RunProperties ??= new();
                            run.RunProperties.RunFonts = new() { Ascii = "Consolas", EastAsia = "宋体" };
                        }
                    }
                    break;
                case ListBlock list:
                    var ordinal = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>()) Write(item, prefix + (list.IsOrdered ? $"{ordinal++}. " : "• "));
                    break;
                case QuoteBlock quote: Write(quote, prefix + "│ "); break;
                case HeadingBlock heading:
                    WriteInline(paragraph(prefix, "heading" + Math.Min(heading.Level, 3)), heading.Inline);
                    break;
                case ParagraphBlock text:
                    var images = text.Inline?.Descendants<LinkInline>().Where(link => link.IsImage).ToArray() ?? [];
                    var textParagraph = paragraph(prefix, "body");
                    WriteInline(textParagraph, text.Inline);
                    if (string.IsNullOrWhiteSpace(textParagraph.InnerText) && images.Length > 0) textParagraph.Remove();
                    foreach (var image in images)
                    {
                        var path = MarkdownSupport.LocalPath(image.Url, directory)
                            ?? throw new InvalidDataException("图片预览和导出只读取本地文件。请先导入远程图片。");
                        appendImage(new() { Type = "image", Path = path, Caption = MarkdownSupport.PlainText(image), AltText = MarkdownSupport.PlainText(image) });
                    }
                    break;
                case ThematicBreakBlock: paragraph("────────────────────", "body"); break;
                case LeafBlock leaf: paragraph(leaf.Lines.ToString(), "body"); break;
                case ContainerBlock nested: Write(nested, prefix); break;
            }
        }
    }

    private void WriteInline(OpenXmlCompositeElement parent, ContainerInline? content, bool bold = false, bool italic = false, bool strike = false, string? vertical = null)
    {
        if (content is null) return;
        foreach (var inline in content)
        {
            switch (inline)
            {
                case EmphasisInline emphasis:
                    WriteInline(parent, emphasis, bold || (emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 2),
                        italic || (emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount == 1),
                        strike || emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2,
                        emphasis.DelimiterChar == '^' ? "superscript" : emphasis.DelimiterChar == '~' && emphasis.DelimiterCount == 1 ? "subscript" : vertical);
                    break;
                case LinkInline link when link.IsImage: break;
                case LinkInline link:
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var target) && target.Scheme is "https" or "http" or "mailto")
                    {
                        var hyperlink = new Hyperlink { Id = main.AddHyperlinkRelationship(target, true).Id };
                        WriteInline(hyperlink, link, bold, italic, strike, vertical);
                        parent.Append(hyperlink);
                    }
                    else WriteInline(parent, link, bold, italic, strike, vertical);
                    break;
                case LineBreakInline: parent.Append(new Run(new Break())); break;
                case ContainerInline nested: WriteInline(parent, nested, bold, italic, strike, vertical); break;
                default:
                    var value = inline is LiteralInline literal ? literal.Content.ToString() : inline is CodeInline code ? code.Content
                        : inline is Markdig.Extensions.Mathematics.MathInline math ? "$" + math.Content.ToString() + "$" : inline.ToString() ?? "";
                    // Generated documents can use direct formatting instead of named styles.
                    // Carry that base format into every Markdown run, including runs inside links.
                    var owner = parent as Paragraph ?? parent.Ancestors<Paragraph>().FirstOrDefault();
                    var properties = owner?.Elements<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties ?? new RunProperties();
                    if (bold) properties.Bold = new();
                    if (italic) properties.Italic = new();
                    if (strike) properties.Strike = new();
                    if (vertical is not null) properties.VerticalTextAlignment = new() { Val = vertical == "superscript" ? VerticalPositionValues.Superscript : VerticalPositionValues.Subscript };
                    if (inline is CodeInline) properties.RunFonts = new() { Ascii = "Consolas" };
                    parent.Append(new Run(properties, new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
                    break;
            }
        }
    }
}
