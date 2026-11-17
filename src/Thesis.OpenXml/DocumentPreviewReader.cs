using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;
using A = DocumentFormat.OpenXml.Drawing;

namespace Thesis.OpenXml;

/// <summary>Read-only structural preview, deliberately independent of Office pagination.</summary>
public static class DocumentPreviewReader
{
    public static DocumentPreview Read(string path, CancellationToken cancellationToken = default)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("文档缺少正文。");
        var body = main.Document?.Body ?? throw new InvalidDataException("文档缺少正文。");
        var result = new DocumentPreview();
        long imageBytes = 0;
        for (var index = 0; index < body.ChildElements.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = body.ChildElements[index];
            if (block is Table table)
            {
                var rows = table.Elements<TableRow>().Select(r => r.Elements<TableCell>().Select(c => string.Join("\n", c.Elements<Paragraph>().Select(p => p.InnerText))).ToList()).ToList();
                foreach (var chunk in rows.Chunk(30)) result.Blocks.Add(new() { SourceIndex = index, Kind = "table", Rows = chunk.ToList() });
                if (rows.Count > 30) result.Notes.Add("长表格按 30 行分段显示，避免一次加载全部单元格；分段位置不是实际分页。");
                if (table.Descendants<GridSpan>().Any() || table.Descendants<VerticalMerge>().Any()) result.Notes.Add("合并单元格在快速预览中按内容展开，DOCX 保留原结构。");
            }
            else if (block is Paragraph p)
            {
                var format = p.ParagraphProperties;
                var styleId = format?.ParagraphStyleId?.Val?.Value;
                var style = main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId == styleId);
                var rPr = p.Descendants<Run>().FirstOrDefault()?.RunProperties;
                var sizeText = rPr?.FontSize?.Val?.Value ?? style?.StyleRunProperties?.GetFirstChild<FontSize>()?.Val?.Value;
                var size = double.TryParse(sizeText, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n / 2 : 12;
                var heading = format?.OutlineLevel?.Val?.Value ?? style?.StyleParagraphProperties?.GetFirstChild<OutlineLevel>()?.Val?.Value;
                if (heading is null && styleId?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true && int.TryParse(styleId[7..], out var level)) heading = level - 1;
                var runs = new List<PreviewRun>();
                foreach (var run in p.Descendants<Run>())
                {
                    var text = string.Concat(run.ChildElements.Select(e => e is Text t ? t.Text : e is Break ? "\n" : e is TabChar ? "\t" : ""));
                    if (text.Length == 0) continue;
                    var rp = run.RunProperties;
                    var link = run.Ancestors<Hyperlink>().FirstOrDefault();
                    runs.Add(new(text, (rp?.Bold is { } b && (b.Val?.Value ?? true)) || heading is not null,
                        rp?.Italic is { } i && (i.Val?.Value ?? true), rp?.Strike is { } s && (s.Val?.Value ?? true),
                        main.HyperlinkRelationships.FirstOrDefault(r => r.Id == link?.Id)?.Uri.ToString()));
                }
                var math = p.Descendants().Where(e => e.LocalName == "oMath" && !e.Ancestors().Any(a => a.LocalName == "oMath")).ToArray();
                foreach (var equation in math) runs.Add(new("  [公式] " + equation.InnerText));
                if (runs.Count > 0) result.Blocks.Add(new() { SourceIndex = index, Kind = heading is null ? "paragraph" : "heading",
                    Font = rPr?.RunFonts?.EastAsia?.Value ?? style?.StyleRunProperties?.GetFirstChild<RunFonts>()?.EastAsia?.Value ?? "宋体",
                    Size = Math.Clamp(heading is null ? size : Math.Max(size, 18 - heading.Value * 2), 5, 72),
                    Alignment = format?.Justification?.Val?.InnerText ?? "left", Runs = runs });
                foreach (var blip in p.Descendants<A.Blip>())
                {
                    if (blip.Embed?.Value is not string id || main.GetPartById(id) is not ImagePart image) continue;
                    using var stream = image.GetStream();
                    if (stream.Length > 8 * 1024 * 1024 || imageBytes + stream.Length > 48 * 1024 * 1024)
                    { result.Blocks.Add(new() { SourceIndex = index, Runs = [new("[大图：请查看成品 DOCX]")] }); continue; }
                    using var buffer = new MemoryStream(); stream.CopyTo(buffer); imageBytes += buffer.Length;
                    result.Blocks.Add(new() { SourceIndex = index, Kind = "image", Image = buffer.ToArray() });
                }
            }
            else if (block is not SectionProperties && block.InnerText.Length > 0)
                result.Blocks.Add(new() { SourceIndex = index, Runs = [new(block.InnerText)] });
        }
        result.Notes.Add("快速预览展示内容、图片、表格与主要样式；分页、页眉页脚、浮动对象和复杂公式请以导出 PDF 为准。");
        return result;
    }
}
