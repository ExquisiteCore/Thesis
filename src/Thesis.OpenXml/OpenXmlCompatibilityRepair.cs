using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

public static class OpenXmlCompatibilityRepair
{
    /// <summary>Restore schema property order after Office saves without changing property values.</summary>
    public static bool NormalizeStyleOrder(string path)
    {
        using var doc = WordprocessingDocument.Open(path, true);
        var changed = false;
        if (doc.MainDocumentPart?.StyleDefinitionsPart?.Styles is Styles styles)
        {
            foreach (var style in styles.Elements<Style>()) changed |= CanonicalizeProperties(style);
            styles.Save();
        }
        if (doc.MainDocumentPart?.DocumentSettingsPart?.Settings is Settings settings)
        {
            changed |= CanonicalizeProperties(settings);
            settings.Save();
        }
        foreach (var part in Parts(doc.MainDocumentPart!))
        {
            if (part.RootElement is null) continue;
            foreach (var element in part.RootElement.Descendants())
            {
                if (element.LocalName is "jc" or "lvlJc")
                {
                    var attribute = element.GetAttributes().FirstOrDefault(a => a.LocalName == "val");
                    if (attribute.Value is not ("start" or "end")) continue;
                    var rtl = element.Parent?.Descendants().Any(e => e.LocalName == "bidi" && e.GetAttributes().All(a => a.LocalName != "val" || a.Value is not ("0" or "false"))) == true;
                    var value = (attribute.Value == "start") != rtl ? "left" : "right";
                    element.SetAttribute(new DocumentFormat.OpenXml.OpenXmlAttribute(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, value));
                    changed = true;
                }
                if (element.LocalName == "charset")
                    foreach (var attribute in element.GetAttributes().Where(a => a.LocalName == "characterSet" && a.Value == "windows-1252").ToArray())
                    { element.RemoveAttribute(attribute.LocalName, attribute.NamespaceUri); changed = true; }
            }
            part.RootElement.Save();
        }
        return changed;
    }

    private static bool CanonicalizeProperties(OpenXmlCompositeElement element)
    {
        var children = element.ChildElements.ToArray();
        var skeleton = (OpenXmlCompositeElement)element.CloneNode(false);
        foreach (var group in children.GroupBy(c => (c.NamespaceUri, c.LocalName)))
            if (!skeleton.AddChild(group.First().CloneNode(true), false)) skeleton.Append(group.First().CloneNode(true));
        var order = skeleton.ChildElements.Select((c, i) => (Key: (c.NamespaceUri, c.LocalName), Index: i)).ToDictionary(p => p.Key, p => p.Index);
        // AddChild may replace repeated children; use it only to discover order, then retain every original node.
        var sorted = children.OrderBy(c => order[(c.NamespaceUri, c.LocalName)]).ToArray();
        if (children.SequenceEqual(sorted)) return false;
        element.RemoveAllChildren();
        element.Append(sorted);
        return true;
    }

    private static IEnumerable<OpenXmlPart> Parts(OpenXmlPart root)
    {
        var seen = new HashSet<OpenXmlPart>();
        var pending = new Stack<OpenXmlPart>();
        pending.Push(root);
        while (pending.TryPop(out var part))
        {
            if (!seen.Add(part)) continue;
            yield return part;
            foreach (var child in part.Parts) pending.Push(child.OpenXmlPart);
        }
    }
}
