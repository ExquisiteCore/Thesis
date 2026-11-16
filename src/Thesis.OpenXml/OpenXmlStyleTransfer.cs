using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

internal static class OpenXmlStyleTransfer
{
    public static Dictionary<string, string> Copy(MainDocumentPart source, MainDocumentPart target, string prefix)
    {
        var map = new Dictionary<string, string>();
        if (source.StyleDefinitionsPart?.Styles is not Styles from) return map;
        var to = target.StyleDefinitionsPart ?? target.AddNewPart<StyleDefinitionsPart>();
        to.Styles ??= new Styles();
        foreach (var style in from.Elements<Style>().Where(s => s.StyleId is not null))
        {
            var candidate = prefix + style.StyleId;
            while (to.Styles.Elements<Style>().Any(s => s.StyleId?.Value == candidate) || map.Values.Contains(candidate)) candidate += "_";
            map[style.StyleId!.Value!] = candidate;
        }
        foreach (var style in from.Elements<Style>().Where(s => s.StyleId is not null))
        {
            var copy = (Style)style.CloneNode(true);
            copy.StyleId = map[style.StyleId!.Value!];
            copy.Default = false;
            Remap(copy, map, "basedOn", "next", "link");
            to.Styles.Append(copy);
        }
        to.Styles.Save();
        return map;
    }

    public static void Remap(OpenXmlElement block, Dictionary<string, string> map, params string[] elementNames)
    {
        if (elementNames.Length == 0) elementNames = ["pStyle", "rStyle", "tblStyle"];
        foreach (var element in block.Descendants().Where(e => elementNames.Contains(e.LocalName)))
        {
            var attr = element.GetAttributes().FirstOrDefault(a => a.LocalName == "val");
            if (map.TryGetValue(attr.Value ?? "", out var mapped))
                element.SetAttribute(new OpenXmlAttribute("w", "val", attr.NamespaceUri, mapped));
        }
    }
}
