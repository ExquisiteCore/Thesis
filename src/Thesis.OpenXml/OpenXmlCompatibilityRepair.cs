using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

public static class OpenXmlCompatibilityRepair
{
    private const string VmlNamespace = "urn:schemas-microsoft-com:vml";

    public static bool NormalizeVmlShapeIds(string path)
    {
        using var document = WordprocessingDocument.Open(path, true);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        if (document.MainDocumentPart is null) return false;
        foreach (var part in Parts(document.MainDocumentPart))
        {
            var root = part.RootElement;
            if (root is null) continue;
            var partChanged = false;
            foreach (var shape in root.Descendants().Where(element => element.NamespaceUri == VmlNamespace && element.LocalName == "shape"))
            {
                var attribute = shape.GetAttributes().FirstOrDefault(candidate => candidate.LocalName == "id" && candidate.NamespaceUri.Length == 0);
                if (string.IsNullOrWhiteSpace(attribute.Value)) continue;
                var original = attribute.Value;
                if (used.Add(original)) continue;

                var suffix = 2;
                string unique;
                do unique = $"{original}_thesis_{suffix++}"; while (!used.Add(unique));
                shape.SetAttribute(new OpenXmlAttribute(string.Empty, "id", string.Empty, unique));
                changed = true;
                partChanged = true;
            }
            if (partChanged) root.Save();
        }
        return changed;
    }

    /// <summary>Repair legacy Word packages that reuse bookmark IDs.</summary>
    public static bool NormalizeBookmarkIds(string path)
    {
        using var document = WordprocessingDocument.Open(path, true);
        if (document.MainDocumentPart?.Document?.Body is not Body body) return false;
        var starts = body.Descendants<BookmarkStart>().ToArray();
        var endNodes = body.Descendants<BookmarkEnd>().ToArray();
        var ends = endNodes.GroupBy(end => end.Id?.Value ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<BookmarkEnd>(group), StringComparer.Ordinal);
        var ids = starts.Select(start => long.TryParse(start.Id?.Value, out var id) ? id : 0)
            .Concat(endNodes.Select(end => long.TryParse(end.Id?.Value, out var id) ? id : 0));
        var next = ids.DefaultIfEmpty(0).Max() + 1;
        var changed = false;
        foreach (var group in starts.GroupBy(start => start.Id?.Value ?? "", StringComparer.Ordinal))
        {
            var originalId = group.Key;
            ends.TryGetValue(originalId, out var queue);
            foreach (var start in group)
            {
                if (queue is null || queue.Count == 0)
                {
                    // A start without a matching end is not a usable bookmark.
                    start.Remove();
                    changed = true;
                    continue;
                }

                // Always allocate a fresh ID. This also separates unique IDs that
                // collide with a legacy range copied from another document part.
                var id = next++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                start.Id = id;
                queue.Dequeue().Id = id;
                changed = true;
            }
        }
        // Any end nodes left in the queues had no corresponding start node.
        foreach (var orphan in ends.Values.SelectMany(queue => queue).ToArray())
        {
            orphan.Remove();
            changed = true;
        }
        if (changed) document.MainDocumentPart.Document.Save();
        return changed;
    }

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
