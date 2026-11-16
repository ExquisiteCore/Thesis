using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Thesis.OpenXml;

internal static class OpenXmlPartTransfer
{
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static void Copy(MainDocumentPart source, MainDocumentPart target, OpenXmlElement block, Dictionary<string, string> map)
    {
        foreach (var element in block.Descendants().Prepend(block))
        foreach (var attribute in element.GetAttributes().Where(x => x.NamespaceUri == R).ToArray())
        {
            if (string.IsNullOrEmpty(attribute.Value)) continue;
            if (!map.TryGetValue(attribute.Value, out var newId))
            {
                var part = source.Parts.FirstOrDefault(x => x.RelationshipId == attribute.Value);
                if (part.OpenXmlPart is not null) newId = target.GetIdOfPart(target.AddPart(part.OpenXmlPart));
                else
                {
                    var link = source.HyperlinkRelationships.FirstOrDefault(x => x.Id == attribute.Value);
                    if (link is not null) newId = target.AddHyperlinkRelationship(link.Uri, link.IsExternal).Id;
                    else
                    {
                        var external = source.ExternalRelationships.FirstOrDefault(x => x.Id == attribute.Value)
                            ?? throw new InvalidDataException($"Unresolved source relationship {attribute.Value}.");
                        newId = target.AddExternalRelationship(external.RelationshipType, external.Uri).Id;
                    }
                }
                map.Add(attribute.Value, newId);
            }
            element.SetAttribute(new OpenXmlAttribute(attribute.Prefix, attribute.LocalName, R, newId));
        }
    }
}
