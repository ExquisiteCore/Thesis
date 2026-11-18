using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

/// <summary>Use actual template formatting, including inherited properties and direct overrides.</summary>
internal static class TemplateRoleStyleBuilder
{
    public static Style Build(MainDocumentPart main, string id, TemplateRoleFormat role)
    {
        var blocks = main.Document!.Body!.ChildElements;
        if (role.TemplateParagraphIndex is not int index || index < 0 || index >= blocks.Count || blocks[index] is not Paragraph sample)
            throw new ArgumentException($"Role {id} must reference a template body paragraph.");
        var styles = main.StyleDefinitionsPart!.Styles!;
        var pPr = new StyleParagraphProperties();
        var rPr = new StyleRunProperties();
        Merge(pPr, styles.GetFirstChild<DocDefaults>()?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle);
        Merge(rPr, styles.GetFirstChild<DocDefaults>()?.RunPropertiesDefault?.RunPropertiesBaseStyle);
        var chain = new Stack<Style>();
        var visited = new HashSet<string>();
        var styleId = sample.ParagraphProperties?.ParagraphStyleId?.Val?.Value
            ?? styles.Elements<Style>().FirstOrDefault(s => s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true)?.StyleId?.Value;
        while (styleId is not null)
        {
            if (!visited.Add(styleId)) throw new InvalidDataException($"Cyclic template style inheritance: {styleId}");
            var style = styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId);
            if (style is null) throw new InvalidDataException($"Template style is undefined: {styleId}");
            chain.Push(style);
            styleId = style.BasedOn?.Val?.Value;
        }
        foreach (var style in chain)
        {
            Merge(pPr, style.StyleParagraphProperties);
            Merge(rPr, style.StyleRunProperties);
        }
        Merge(pPr, sample.ParagraphProperties);
        Merge(rPr, sample.ParagraphProperties?.ParagraphMarkRunProperties);
        var dominantRun = TemplateSampleText.DominantRun(sample);
        Merge(rPr, dominantRun?.RunProperties);
        foreach (var property in pPr.ChildElements.Where(e => e.LocalName is "pStyle" or "sectPr" or "numPr" or "rPr").ToArray()) property.Remove();
        rPr.RemoveAllChildren<RunStyle>();
        if (role.OutlineLevel.HasValue)
        {
            pPr.RemoveAllChildren<OutlineLevel>();
            pPr.AddChild(new OutlineLevel { Val = role.OutlineLevel.Value }, true);
            pPr.RemoveAllChildren<KeepNext>();
            pPr.AddChild(new KeepNext(), true);
        }
        else pPr.RemoveAllChildren<OutlineLevel>();
        if (role.PageBreakBefore)
        {
            pPr.RemoveAllChildren<PageBreakBefore>();
            pPr.AddChild(new PageBreakBefore(), true);
        }
        if (role.ApplyTemplateInstructions) TemplateInstructionFormat.Apply(main, id, pPr, rPr);
        return new Style(new StyleName { Val = id }, new NextParagraphStyle { Val = "Thesis_body" }, new PrimaryStyle(), pPr, rPr)
        { Type = StyleValues.Paragraph, StyleId = id };
    }

    private static void Merge(OpenXmlCompositeElement target, OpenXmlElement? source)
    {
        if (source is null) return;
        foreach (var value in source.ChildElements)
        {
            if (value.LocalName is "pStyle" or "rStyle" or "sectPr" or "rPr" or "pPrChange" or "rPrChange" or "ins" or "del") continue;
            var previous = target.ChildElements.FirstOrDefault(e => e.LocalName == value.LocalName && e.NamespaceUri == value.NamespaceUri);
            if (previous is not null && !previous.HasChildren && !value.HasChildren && value.GetAttributes().Count > 0)
            {
                // An eastAsia override must retain inherited ascii/hAnsi fonts; the same applies to spacing/indent attributes.
                foreach (var attribute in value.GetAttributes()) previous.SetAttribute(attribute);
                continue;
            }
            previous?.Remove();
            if (!target.AddChild(value.CloneNode(true), false))
            {
                if (value.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
                    throw new InvalidDataException($"Template format property is invalid in {target.LocalName}: {value.LocalName}");
                target.Append(value.CloneNode(true));
            }
        }
    }
}
