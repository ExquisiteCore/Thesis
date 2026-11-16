using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

/// <summary>All document-scoped mappings live together, so block boundaries never break references.</summary>
internal sealed class OpenXmlSourceTransfer
{
    private readonly MainDocumentPart source;
    private readonly MainDocumentPart target;
    private readonly OpenXmlBookmarkTransfer bookmarks;
    private readonly Dictionary<string, string> styles;
    private readonly Dictionary<int, int> numbering;
    private readonly Dictionary<string, string> relationships = [];

    public OpenXmlSourceTransfer(MainDocumentPart source, MainDocumentPart target, string prefix)
    {
        this.source = source;
        this.target = target;
        bookmarks = new(source, target, prefix + "bookmark_");
        styles = OpenXmlStyleTransfer.Copy(source, target, prefix);
        numbering = OpenXmlNumberingTransfer.Copy(source, target, styles);
    }

    public void Remap(OpenXmlElement clone)
    {
        foreach (var paragraph in clone.Descendants<Paragraph>().Prepend(clone as Paragraph).OfType<Paragraph>())
        {
            if (paragraph.ParagraphProperties?.NumberingProperties is not null) continue;
            var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var seen = new HashSet<string>();
            while (id is not null && seen.Add(id))
            {
                var style = source.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
                if (style?.StyleParagraphProperties?.NumberingProperties is NumberingProperties properties)
                {
                    paragraph.ParagraphProperties ??= new ParagraphProperties();
                    paragraph.ParagraphProperties.AddChild(properties.CloneNode(true), true);
                    break;
                }
                id = style?.BasedOn?.Val?.Value;
            }
        }
        bookmarks.Remap(clone);
        OpenXmlStyleTransfer.Remap(clone, styles);
        OpenXmlNumberingTransfer.Remap(clone, numbering);
        OpenXmlPartTransfer.Copy(source, target, clone, relationships);
    }
}
