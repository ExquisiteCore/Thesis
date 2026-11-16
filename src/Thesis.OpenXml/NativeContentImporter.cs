using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

/// <summary>Imports ordered, fingerprint-pinned blocks without flattening inline objects or merged cells.</summary>
internal sealed class NativeContentImporter(MainDocumentPart target) : IDisposable
{
    private readonly Dictionary<string, Source> sources = new(Thesis.Core.FileOutputTransaction.PathComparer);
    public OpenXmlElement Import(ThesisContentBlock block, ParagraphFormatSample? paragraphFormat, TableFormatSample? tableFormat)
    {
        if (block.SourcePath is null || block.SourceSha256 is null || block.SourceBodyElementIndex is null)
            throw new InvalidDataException("Native content needs sourcePath, sourceSha256 and sourceBodyElementIndex.");
        var path = Path.GetFullPath(block.SourcePath);
        if (!sources.TryGetValue(path, out var source))
        {
            using var bytes = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(block.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Native content source changed: {path}");
            source = new Source(WordprocessingDocument.Open(path, false), hash);
            sources.Add(path, source);
            source.Transfer = new OpenXmlSourceTransfer(source.Document.MainDocumentPart!, target, "native" + sources.Count + "_");
        }
        if (!source.Hash.Equals(block.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Conflicting native source fingerprints.");
        var children = source.Document.MainDocumentPart!.Document!.Body!.ChildElements;
        var index = block.SourceBodyElementIndex.Value;
        if (index < 0 || index >= children.Count || children[index] is SectionProperties) throw new InvalidDataException($"Invalid native block index: {index}");
        var clone = children[index].CloneNode(true);
        var unsupported = Unsupported(clone, source.Document.MainDocumentPart, allowMigratableReferences: true);
        if (unsupported.Count > 0) throw new InvalidDataException($"Native block {index} needs migration support for: {string.Join(", ", unsupported)}");
        foreach (var p in clone.Descendants<Paragraph>().Prepend(clone as Paragraph).OfType<Paragraph>())
            p.ParagraphProperties?.RemoveAllChildren<SectionProperties>();
        foreach (var el in clone.Descendants().Where(e => e.LocalName is "commentRangeStart" or "commentRangeEnd" or "commentReference" or "proofErr").ToArray()) el.Remove();
        source.Transfer!.Remap(clone);
        if (clone is Paragraph paragraph && paragraphFormat is not null) OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, paragraphFormat);
        if (clone is Table table && tableFormat is not null) OpenXmlFormatApplier.ApplyTableFormat(table, tableFormat);
        foreach (var prop in clone.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>())
            prop.Id = (uint)(100000 + ++drawingId);
        return clone;
    }

    private int drawingId;
    public static List<string> Unsupported(OpenXmlElement block, MainDocumentPart? source = null, bool allowMigratableReferences = true)
    {
        var features = new HashSet<string>();
        foreach (var e in block.Descendants().Prepend(block))
        {
            if (e.LocalName is "footnoteReference" or "endnoteReference" or "altChunk") features.Add(e.LocalName);
            if (e.LocalName == "OLEObject" && !block.Descendants().Any(n => n.LocalName == "imagedata")) features.Add("embedded object without preview");
            if (!allowMigratableReferences && e is FieldCode field && System.Text.RegularExpressions.Regex.IsMatch(field.Text, @"\b(REF|PAGEREF|NOTEREF)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) features.Add("bookmarks/cross-references");
            if (!allowMigratableReferences && e is SimpleField simple && System.Text.RegularExpressions.Regex.IsMatch(simple.Instruction?.Value ?? "", @"\b(REF|PAGEREF|NOTEREF)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) features.Add("bookmarks/cross-references");
        }
        if (!allowMigratableReferences)
        {
            var starts = block.Descendants<BookmarkStart>().Select(b => b.Id?.Value).ToList();
            var ends = block.Descendants<BookmarkEnd>().Select(b => b.Id?.Value).ToList();
            if (starts.Any(id => id is null) || starts.Count != starts.Distinct().Count() || ends.Count != ends.Distinct().Count()
                || !starts.Order().SequenceEqual(ends.Order())) features.Add("bookmarks/cross-references spanning body blocks");
        }
        return features.Order().ToList();
    }

    public void Dispose() { foreach (var source in sources.Values) source.Document.Dispose(); }
    private sealed class Source(WordprocessingDocument doc, string hash)
    {
        public WordprocessingDocument Document { get; } = doc;
        public string Hash { get; } = hash;
        public OpenXmlSourceTransfer? Transfer { get; set; }
    }
}
