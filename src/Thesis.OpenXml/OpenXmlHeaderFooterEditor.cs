using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlHeaderFooterEditor
{
    public static void SetHeaderFooterText(MainDocumentPart mainPart, Body body, string kind, string type, string text)
    {
        var relationshipId = kind == "footer"
            ? EnsureFooter(mainPart, type)
            : EnsureHeader(mainPart, type);
        EnsureSectionReference(body, kind, type, relationshipId);
        var paragraph = new Paragraph(new Run(new Text(text)));

        if (kind == "footer")
        {
            var part = (FooterPart)mainPart.GetPartById(relationshipId);
            part.Footer = new Footer(paragraph);
            part.Footer.Save();
            return;
        }

        var headerPart = (HeaderPart)mainPart.GetPartById(relationshipId);
        headerPart.Header = new Header(paragraph);
        headerPart.Header.Save();
    }

    public static void InsertPageNumber(MainDocumentPart mainPart, Body body, string kind, string type, string? alignment)
    {
        var relationshipId = kind == "header"
            ? EnsureHeader(mainPart, type)
            : EnsureFooter(mainPart, type);
        EnsureSectionReference(body, kind, type, relationshipId);

        var paragraph = new Paragraph();
        if (!string.IsNullOrWhiteSpace(alignment))
        {
            paragraph.ParagraphProperties = new ParagraphProperties(new Justification { Val = ParseJustification(alignment) });
        }

        paragraph.Append(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

        if (kind == "header")
        {
            var headerPart = (HeaderPart)mainPart.GetPartById(relationshipId);
            headerPart.Header ??= new Header();
            headerPart.Header.RemoveAllChildren<Paragraph>();
            headerPart.Header.AppendChild(paragraph);
            headerPart.Header.Save();
            return;
        }

        var footerPart = (FooterPart)mainPart.GetPartById(relationshipId);
        footerPart.Footer ??= new Footer();
        footerPart.Footer.RemoveAllChildren<Paragraph>();
        footerPart.Footer.AppendChild(paragraph);
        footerPart.Footer.Save();
    }

    private static string EnsureHeader(MainDocumentPart mainPart, string type)
    {
        var document = mainPart.Document ?? throw new InvalidDataException("Main document is missing.");
        var body = document.Body;
        if (body is null)
        {
            throw new InvalidDataException("Document body is missing.");
        }
        var section = body.GetFirstChild<SectionProperties>();
        var existing = section?.Elements<HeaderReference>()
            .FirstOrDefault(reference => ReferenceTypeMatches(reference.Type?.Value, type));
        if (!string.IsNullOrWhiteSpace(existing?.Id?.Value))
        {
            return existing.Id!.Value!;
        }

        var part = mainPart.AddNewPart<HeaderPart>();
        part.Header = new Header(new Paragraph());
        part.Header.Save();
        var id = mainPart.GetIdOfPart(part);
        EnsureSectionReference(body, "header", type, id);
        return id;
    }

    private static string EnsureFooter(MainDocumentPart mainPart, string type)
    {
        var document = mainPart.Document ?? throw new InvalidDataException("Main document is missing.");
        var body = document.Body;
        if (body is null)
        {
            throw new InvalidDataException("Document body is missing.");
        }
        var section = body.GetFirstChild<SectionProperties>();
        var existing = section?.Elements<FooterReference>()
            .FirstOrDefault(reference => ReferenceTypeMatches(reference.Type?.Value, type));
        if (!string.IsNullOrWhiteSpace(existing?.Id?.Value))
        {
            return existing.Id!.Value!;
        }

        var part = mainPart.AddNewPart<FooterPart>();
        part.Footer = new Footer(new Paragraph());
        part.Footer.Save();
        var id = mainPart.GetIdOfPart(part);
        EnsureSectionReference(body, "footer", type, id);
        return id;
    }

    private static void EnsureSectionReference(Body body, string kind, string type, string relationshipId)
    {
        var section = body.GetFirstChild<SectionProperties>();
        if (section is null)
        {
            section = new SectionProperties();
            body.AppendChild(section);
        }

        var headerFooterType = ParseHeaderFooterType(type);
        if (kind == "header")
        {
            section.RemoveAllChildren<HeaderReference>();
            section.PrependChild(new HeaderReference { Type = headerFooterType, Id = relationshipId });
            return;
        }

        section.RemoveAllChildren<FooterReference>();
        section.PrependChild(new FooterReference { Type = headerFooterType, Id = relationshipId });
    }

    private static bool ReferenceTypeMatches(HeaderFooterValues? actual, string expected)
    {
        return actual == ParseHeaderFooterType(expected);
    }

    private static HeaderFooterValues ParseHeaderFooterType(string type)
    {
        return type switch
        {
            "first" => HeaderFooterValues.First,
            "even" => HeaderFooterValues.Even,
            _ => HeaderFooterValues.Default
        };
    }

    private static JustificationValues ParseJustification(string alignment)
    {
        return alignment switch
        {
            "left" => JustificationValues.Left,
            "right" => JustificationValues.Right,
            "both" => JustificationValues.Both,
            _ => JustificationValues.Center
        };
    }
}
