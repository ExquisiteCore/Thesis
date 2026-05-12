using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlParagraphTools
{
    public static void ApplyPageBreakBefore(Paragraph paragraph, bool value)
    {
        var properties = GetOrCreateParagraphProperties(paragraph);
        if (value)
        {
            properties.PageBreakBefore ??= new PageBreakBefore();
        }
        else
        {
            properties.PageBreakBefore?.Remove();
        }
    }

    public static void ClearDirectFormatting(Paragraph paragraph, string scope)
    {
        if (scope is "paragraph" or "paragraphAndRuns")
        {
            paragraph.ParagraphProperties?.RemoveAllChildren<Justification>();
            paragraph.ParagraphProperties?.RemoveAllChildren<SpacingBetweenLines>();
            paragraph.ParagraphProperties?.RemoveAllChildren<Indentation>();
            paragraph.ParagraphProperties?.RemoveAllChildren<PageBreakBefore>();
            if (paragraph.ParagraphProperties is { HasChildren: false })
            {
                paragraph.ParagraphProperties.Remove();
            }
        }

        if (scope is "runs" or "paragraphAndRuns")
        {
            foreach (var properties in paragraph.Descendants<RunProperties>().ToList())
            {
                properties.Remove();
            }
        }
    }

    public static Paragraph CreateTextParagraph(string text, ParagraphFormatSample? format = null)
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));

        if (format is not null)
        {
            OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, format);
        }

        return paragraph;
    }

    private static ParagraphProperties GetOrCreateParagraphProperties(Paragraph paragraph)
    {
        if (paragraph.ParagraphProperties is not null)
        {
            return paragraph.ParagraphProperties;
        }

        var properties = new ParagraphProperties();
        paragraph.PrependChild(properties);
        return properties;
    }

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }
}
