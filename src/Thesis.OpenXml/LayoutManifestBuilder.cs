using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

public static class LayoutManifestBuilder
{
    public static object Build(string path, IReadOnlyList<string> headings)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var pending = new List<string>();
        var formats = new Dictionary<string, string>();
        foreach (var block in doc.MainDocumentPart!.Document!.Body!.ChildElements)
        {
            if (block is Paragraph p && headings.Contains(p.InnerText)) pending.Add(p.InnerText);
            var section = block as SectionProperties ?? block.Descendants<SectionProperties>().LastOrDefault();
            if (section is null) continue;
            var format = section.GetFirstChild<PageNumberType>()?.Format?.InnerText ?? "decimal";
            foreach (var heading in pending) formats[heading] = format;
            pending.Clear();
        }
        return new { Headings = headings, PageFormats = formats };
    }
}
