using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class EditableMarkdownBuilder
{
    public static string Convert(List<string> paragraphs, List<ThesisContentBlock> blocks, List<ThesisTableContent> tables)
    {
        var documents = new Dictionary<string, WordprocessingDocument>(FileOutputTransaction.PathComparer);
        var fingerprints = new Dictionary<string, string>(FileOutputTransaction.PathComparer);
        try
        {
            var converted = new List<ThesisContentBlock>();
            foreach (var block in blocks)
            {
                if (block.Type != "native" || block.SourcePath is null || block.SourceBodyElementIndex is not int index) { converted.Add(block); continue; }
                if (!documents.TryGetValue(block.SourcePath, out var doc))
                {
                    documents[block.SourcePath] = doc = WordprocessingDocument.Open(block.SourcePath, false);
                    fingerprints[block.SourcePath] = TemplatePlanPreflight.Fingerprint(block.SourcePath);
                }
                if (!string.Equals(fingerprints[block.SourcePath], block.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("原稿快照已变化，请重新导入。");
                var children = doc.MainDocumentPart!.Document!.Body!.ChildElements;
                if (index < 0 || index >= children.Count) throw new InvalidDataException("原稿片段索引失效。");
                if (children[index] is not Paragraph paragraph || paragraph.Descendants().Any(e => e.LocalName is "drawing" or "pict" or "object" or "oMath" or "fldChar" or "fldSimple" or "footnoteReference" or "endnoteReference" or "hyperlink" or "ins" or "del"))
                { converted.Add(block); continue; }
                string Escape(string value)
                {
                    foreach (var token in new[] { "\\", "`", "*", "_", "{", "}", "[", "]", "<", ">", "#", "|", "$", "~", "^" }) value = value.Replace(token, "\\" + token);
                    return value;
                }
                var text = string.Concat(paragraph.Elements<Run>().Select(r =>
                {
                    var value = Escape(r.InnerText);
                    if (r.RunProperties?.Bold is { } b && (b.Val?.Value ?? true) && value.Trim().Length > 0) value = "**" + value + "**";
                    if (r.RunProperties?.Italic is { } i && (i.Val?.Value ?? true) && value.Trim().Length > 0) value = "*" + value + "*";
                    return value;
                }));
                var prefix = block.Role switch { "heading1" => "# ", "heading2" => "## ", "heading3" => "### ", _ => "" };
                converted.Add(new() { Type = "paragraph", Text = prefix + text });
            }
            return MarkdownSupport.Body(paragraphs, converted, tables);
        }
        finally { foreach (var document in documents.Values) document.Dispose(); }
    }
}
