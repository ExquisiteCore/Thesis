using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

/// <summary>One mapping per source document, shared by imported blocks and field instructions.</summary>
internal sealed class OpenXmlBookmarkTransfer
{
    private readonly Dictionary<string, string> ids = [];
    private readonly Dictionary<string, string> names = [];
    private readonly Stack<List<FieldCode>> fieldContext = new();
    private sealed class Allocation { public long Next = 1; }
    private static readonly ConditionalWeakTable<MainDocumentPart, Allocation> Allocations = new();

    public OpenXmlBookmarkTransfer(MainDocumentPart source, MainDocumentPart target, string prefix)
    {
        var allocation = Allocations.GetOrCreateValue(target);
        lock (allocation)
        {
            allocation.Next = Math.Max(allocation.Next, target.Document!.Descendants<BookmarkStart>().Select(b => long.TryParse(b.Id?.Value, out var n) ? n : 0).DefaultIfEmpty().Max() + 1);
            var existing = target.Document.Descendants<BookmarkStart>().Select(b => b.Name?.Value).ToHashSet();
            foreach (var start in source.Document!.Descendants<BookmarkStart>())
            {
                var oldId = start.Id?.Value ?? throw new InvalidDataException("Bookmark has no ID.");
                var oldName = start.Name?.Value ?? throw new InvalidDataException("Bookmark has no name.");
                var id = (allocation.Next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var name = prefix + id;
                while (!existing.Add(name)) name += "_";
                if (!ids.TryAdd(oldId, id) || !names.TryAdd(oldName, name)) throw new InvalidDataException("Source bookmark IDs and names must be unique.");
            }
        }
    }

    public void Remap(OpenXmlElement block)
    {
        foreach (var start in block.Descendants<BookmarkStart>())
        {
            start.Name = names[start.Name!.Value!];
            start.Id = ids[start.Id!.Value!];
        }
        foreach (var end in block.Descendants<BookmarkEnd>())
            end.Id = ids.TryGetValue(end.Id?.Value ?? "", out var id) ? id : throw new InvalidDataException("Bookmark end has no source start.");
        foreach (var link in block.Descendants<Hyperlink>())
            if (link.Anchor?.Value is string anchor && names.TryGetValue(anchor, out var mapped)) link.Anchor = mapped;
        OpenXmlFieldInstructions.RewriteAll(block, instruction => OpenXmlFieldInstructions.Rewrite(instruction, names), fieldContext);
    }

}
