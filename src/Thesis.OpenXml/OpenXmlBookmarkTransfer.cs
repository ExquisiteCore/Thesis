using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

/// <summary>Rekeys self-contained bookmarks; cross-block/reference graphs need explicit migration.</summary>
internal static class OpenXmlBookmarkTransfer
{
    public static void Remap(MainDocumentPart target, OpenXmlElement clone)
    {
        var next = target.Document!.Descendants<BookmarkStart>().Select(b => long.TryParse(b.Id?.Value, out var id) ? id : 0).DefaultIfEmpty().Max() + 1;
        var ids = new Dictionary<string, string>();
        foreach (var start in clone.Descendants<BookmarkStart>())
        {
            var id = (next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ids[start.Id!.Value!] = id;
            start.Id = id;
            start.Name = "source_bookmark_" + id;
        }
        foreach (var end in clone.Descendants<BookmarkEnd>()) end.Id = ids[end.Id!.Value!];
    }
}
