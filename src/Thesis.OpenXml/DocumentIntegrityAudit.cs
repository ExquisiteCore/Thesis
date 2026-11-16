using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

public static class DocumentIntegrityAudit
{
    public static List<string> Check(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;
        var errors = new List<string>();
        var starts = body.Descendants<BookmarkStart>().ToArray();
        var ends = body.Descendants<BookmarkEnd>().ToArray();
        var names = starts.Select(b => b.Name?.Value ?? "").ToHashSet(StringComparer.Ordinal);
        if (starts.Any(b => string.IsNullOrEmpty(b.Name?.Value) || string.IsNullOrEmpty(b.Id?.Value)) || names.Count != starts.Length)
            errors.Add("Bookmark names must be present and unique.");
        var startIds = starts.Select(b => b.Id?.Value).Order().ToArray();
        var endIds = ends.Select(b => b.Id?.Value).Order().ToArray();
        if (startIds.Distinct().Count() != startIds.Length || !startIds.SequenceEqual(endIds)) errors.Add("Bookmark starts and ends are unbalanced or duplicated.");
        foreach (var link in body.Descendants<Hyperlink>())
            if (link.Anchor?.Value is string anchor && !names.Contains(anchor)) errors.Add($"Internal hyperlink target is missing: {anchor}");
        foreach (var name in OpenXmlFieldInstructions.Targets(body))
        {
            if (!names.Contains(name)) errors.Add($"Cross-reference target is missing: {name}");
        }
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        var lists = numbering?.Elements<NumberingInstance>().Select(n => n.NumberID?.Value).ToHashSet() ?? [];
        foreach (var id in body.Descendants<NumberingId>())
            if (id.Val?.Value is > 0 && !lists.Contains(id.Val.Value)) errors.Add($"List definition is missing: {id.Val}");
        var styleIds = main.StyleDefinitionsPart?.Styles?.Elements<Style>().Select(s => s.StyleId?.Value).ToHashSet() ?? [];
        foreach (var reference in body.Descendants().Where(e => e.LocalName is "pStyle" or "rStyle" or "tblStyle"))
        {
            var id = reference.GetAttributes().FirstOrDefault(a => a.LocalName == "val").Value;
            if (id is null || !styleIds.Contains(id)) errors.Add($"Style definition is missing: {id}");
        }
        var relationships = main.Parts.Select(p => p.RelationshipId).Concat(main.HyperlinkRelationships.Select(r => r.Id))
            .Concat(main.ExternalRelationships.Select(r => r.Id)).ToHashSet();
        foreach (var attribute in body.Descendants().SelectMany(e => e.GetAttributes()).Where(a => a.NamespaceUri == "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))
            if (attribute.Value is null || !relationships.Contains(attribute.Value)) errors.Add($"Package relationship is missing: {attribute.Value}");
        return errors.Distinct().ToList();
    }

    /// <summary>Exclude field instructions and cached field results, which renderers are expected to refresh.</summary>
    public static string AuthoredText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var text = new System.Text.StringBuilder();
        var fields = new Stack<System.Text.StringBuilder>();
        foreach (var element in doc.MainDocumentPart!.Document!.Body!.Descendants())
        {
            if (element is FieldChar field)
            {
                if (field.FieldCharType?.Value == FieldCharValues.Begin) fields.Push(new());
                if (field.FieldCharType?.Value == FieldCharValues.End && fields.Count > 0) fields.Pop();
            }
            if (element is FieldCode code && fields.Count > 0) fields.Peek().Append(code.Text);
            var authored = element switch { Text t => t.Text, DocumentFormat.OpenXml.Math.Text m => m.Text, _ => null };
            if (authored is not null && fields.All(instruction => Regex.IsMatch(instruction.ToString(), @"^\s*HYPERLINK\b", RegexOptions.IgnoreCase))
                && element.Ancestors<SimpleField>().All(f => Regex.IsMatch(f.Instruction?.Value ?? "", @"^\s*HYPERLINK\b", RegexOptions.IgnoreCase))) text.Append(authored);
        }
        return Regex.Replace(text.ToString().Normalize(), @"\s+", "");
    }
}
