using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

internal static class OpenXmlNumberingTransfer
{
    public static Dictionary<int, int> Copy(MainDocumentPart source, MainDocumentPart target, Dictionary<string, string> styles)
    {
        var map = new Dictionary<int, int> { [0] = 0 };
        if (source.NumberingDefinitionsPart?.Numbering is not Numbering numbering) return map;
        if (numbering.Elements<NumberingPictureBullet>().Any()) throw new InvalidDataException("Picture bullets require an explicit numbering mapping.");
        var part = target.NumberingDefinitionsPart ?? target.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering ??= new Numbering();
        var abstractNext = part.Numbering.Elements<AbstractNum>().Select(n => n.AbstractNumberId?.Value ?? 0).DefaultIfEmpty().Max() + 1;
        var next = part.Numbering.Elements<NumberingInstance>().Select(n => n.NumberID?.Value ?? 0).DefaultIfEmpty().Max() + 1;
        var abstractMap = new Dictionary<int, int>();
        foreach (var original in numbering.Elements<AbstractNum>())
        {
            var copy = (AbstractNum)original.CloneNode(true);
            abstractMap[original.AbstractNumberId!.Value] = abstractNext;
            copy.AbstractNumberId = abstractNext++;
            OpenXmlStyleTransfer.Remap(copy, styles, "pStyle", "styleLink", "numStyleLink");
            var firstInstance = part.Numbering.Elements<NumberingInstance>().FirstOrDefault();
            if (firstInstance is null) part.Numbering.Append(copy); else part.Numbering.InsertBefore(copy, firstInstance);
        }
        foreach (var original in numbering.Elements<NumberingInstance>())
        {
            var copy = (NumberingInstance)original.CloneNode(true);
            map[original.NumberID!.Value] = next;
            copy.NumberID = next++;
            var reference = copy.GetFirstChild<AbstractNumId>() ?? throw new InvalidDataException("Numbering has no abstract definition.");
            if (!abstractMap.TryGetValue(reference.Val!.Value, out var mapped)) throw new InvalidDataException("Numbering references a missing abstract definition.");
            reference.Val = mapped;
            var cleanup = part.Numbering.GetFirstChild<NumberingIdMacAtCleanup>();
            if (cleanup is null) part.Numbering.Append(copy); else part.Numbering.InsertBefore(copy, cleanup);
        }
        if (target.StyleDefinitionsPart?.Styles is Styles targetStyles)
            foreach (var style in targetStyles.Elements<Style>().Where(s => styles.Values.Contains(s.StyleId?.Value ?? ""))) Remap(style, map);
        part.Numbering.Save();
        return map;
    }

    public static void Remap(OpenXmlElement block, Dictionary<int, int> map)
    {
        foreach (var id in block.Descendants<NumberingId>())
        {
            if (id.Val is null || !map.TryGetValue(id.Val.Value, out var mapped)) throw new InvalidDataException("Numbering references an undefined source list.");
            id.Val = mapped;
        }
    }
}
