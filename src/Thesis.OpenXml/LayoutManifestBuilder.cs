using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

public static class LayoutManifestBuilder
{
    public static object Build(string path, IReadOnlyList<string> headings, Thesis.Schema.HostApplicationReport? host = null)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var pending = new List<string>();
        var formats = new Dictionary<string, string>();
        var entries = new List<object>();
        foreach (var block in doc.MainDocumentPart!.Document!.Body!.ChildElements)
        {
            if (block is Paragraph p)
            {
                var bookmark = p.Descendants<BookmarkStart>().FirstOrDefault(b => b.Name?.Value?.StartsWith("thesis_target_", StringComparison.Ordinal) == true)
                    ?? p.Descendants<BookmarkStart>().FirstOrDefault(b => b.Name?.Value?.StartsWith("thesis_heading_", StringComparison.Ordinal) == true);
                if (bookmark?.Name?.Value is string key)
                {
                    key = key.Replace("thesis_target_", "thesis_heading_", StringComparison.Ordinal);
                    pending.Add(key);
                    entries.Add(new { Key = key, Text = string.Concat(p.Descendants<Text>().Select(t => t.Text)) });
                }
            }
            var section = block as SectionProperties ?? block.Descendants<SectionProperties>().LastOrDefault();
            if (section is null) continue;
            var format = section.GetFirstChild<PageNumberType>()?.Format?.InnerText ?? "decimal";
            foreach (var heading in pending) formats[heading] = format;
            pending.Clear();
        }
        return new { Headings = headings, HeadingEntries = entries, PageFormats = formats, HostPages = host?.BookmarkPages };
    }
}
