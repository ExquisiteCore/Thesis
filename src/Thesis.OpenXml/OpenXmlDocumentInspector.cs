using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class OpenXmlDocumentInspector
{
    private const int PreviewLimit = 200;

    public static bool TryInspect(string docxPath, out DocumentMap? documentMap, out Diagnostic? diagnostic)
    {
        try
        {
            documentMap = Inspect(docxPath);
            diagnostic = null;
            return true;
        }
        catch (Exception ex) when (IsExpectedInspectionFailure(ex))
        {
            documentMap = null;
            diagnostic = new Diagnostic
            {
                Severity = "warning",
                Code = "document_map_unavailable",
                Message = $"Working document could not be inspected as DOCX: {ex.Message}",
                Path = System.IO.Path.GetFullPath(docxPath)
            };
            return false;
        }
    }

    public static DocumentMap Inspect(string docxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        var fullPath = System.IO.Path.GetFullPath(docxPath);

        using var document = WordprocessingDocument.Open(fullPath, isEditable: false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("DOCX does not contain a main document part.");
        var wordDocument = mainPart.Document
            ?? throw new InvalidDataException("DOCX does not contain a document.");
        var body = wordDocument.Body
            ?? throw new InvalidDataException("DOCX does not contain a document body.");

        var finalizationReasons = GetFinalizationReasons(body);

        return new DocumentMap
        {
            Path = fullPath,
            RequiresFinalization = finalizationReasons.Count > 0,
            FinalizationReasons = finalizationReasons,
            Paragraphs = ReadParagraphs(body),
            Styles = ReadStyles(mainPart),
            Numbering = ReadNumbering(mainPart),
            Sections = ReadSections(body),
            Tables = ReadTables(body)
        };
    }

    private static List<DocumentParagraph> ReadParagraphs(Body body)
    {
        return body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<Table>().Any())
            .Where(paragraph => !IsFieldOnlyParagraph(paragraph))
            .Select((paragraph, index) => new DocumentParagraph
            {
                Index = index,
                Text = paragraph.InnerText,
                StyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value,
                Numbering = ReadParagraphNumbering(paragraph),
                Runs = ReadRuns(paragraph)
            })
            .ToList();
    }

    private static NumberingReference? ReadParagraphNumbering(Paragraph paragraph)
    {
        var numberingProperties = paragraph.ParagraphProperties?.NumberingProperties;
        var numberingId = numberingProperties?.NumberingId?.Val is null
            ? null
            : numberingProperties.NumberingId.Val.Value.ToString();
        var level = numberingProperties?.NumberingLevelReference?.Val is null
            ? null
            : numberingProperties.NumberingLevelReference.Val.Value.ToString();

        if (numberingId is null && level is null)
        {
            return null;
        }

        return new NumberingReference
        {
            NumberingId = numberingId,
            Level = level
        };
    }

    private static List<DocumentRun> ReadRuns(Paragraph paragraph)
    {
        return paragraph
            .Descendants<Run>()
            .Select((run, index) => new DocumentRun
            {
                Index = index,
                Text = run.InnerText,
                Bold = run.RunProperties?.Bold is not null,
                Italic = run.RunProperties?.Italic is not null,
                FontSizeHalfPoints = run.RunProperties?.FontSize?.Val?.Value
            })
            .ToList();
    }

    private static List<DocumentStyle> ReadStyles(MainDocumentPart mainPart)
    {
        return mainPart.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .Select(style => new DocumentStyle
            {
                StyleId = style.StyleId?.Value,
                Name = style.StyleName?.Val?.Value,
                Type = LowerInnerText(style.Type),
                BasedOn = style.BasedOn?.Val?.Value
            })
            .ToList()
            ?? [];
    }

    private static List<DocumentNumbering> ReadNumbering(MainDocumentPart mainPart)
    {
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering is null)
        {
            return [];
        }

        var abstractNumbers = numbering.Elements<AbstractNum>()
            .Where(abstractNumber => abstractNumber.AbstractNumberId?.Value is not null)
            .ToDictionary(
                abstractNumber => abstractNumber.AbstractNumberId!.Value!.ToString(),
                abstractNumber => abstractNumber,
                StringComparer.Ordinal);

        return numbering
            .Elements<NumberingInstance>()
            .Select(instance => new DocumentNumbering
            {
                NumberingId = instance.NumberID?.Value.ToString(),
                AbstractNumberingId = instance.AbstractNumId?.Val?.Value.ToString(),
                Levels = ReadNumberingLevels(instance, abstractNumbers)
            })
            .ToList();
    }

    private static List<DocumentNumberingLevel> ReadNumberingLevels(
        NumberingInstance instance,
        IReadOnlyDictionary<string, AbstractNum> abstractNumbers)
    {
        var abstractNumberingId = instance.AbstractNumId?.Val?.Value.ToString();
        if (abstractNumberingId is null || !abstractNumbers.TryGetValue(abstractNumberingId, out var abstractNumber))
        {
            return [];
        }

        return abstractNumber
            .Elements<Level>()
            .Select(level => new DocumentNumberingLevel
            {
                Level = level.LevelIndex is null ? null : level.LevelIndex.Value.ToString(),
                Format = LowerInnerText(level.NumberingFormat?.Val),
                Text = level.LevelText?.Val?.Value
            })
            .ToList();
    }

    private static List<DocumentSection> ReadSections(Body body)
    {
        var sectionProperties = body.Descendants<SectionProperties>().ToList();

        return sectionProperties
            .Select((section, index) => new DocumentSection
            {
                Index = index,
                PageSize = ReadPageSize(section.GetFirstChild<PageSize>()),
                PageMargin = ReadPageMargin(section.GetFirstChild<PageMargin>()),
                Headers = ReadHeaders(section),
                Footers = ReadFooters(section)
            })
            .ToList();
    }

    private static PageSizeInfo? ReadPageSize(PageSize? pageSize)
    {
        if (pageSize is null)
        {
            return null;
        }

        return new PageSizeInfo
        {
            WidthTwips = ToInt(pageSize.Width),
            HeightTwips = ToInt(pageSize.Height),
            Orientation = LowerInnerText(pageSize.Orient)
        };
    }

    private static PageMarginInfo? ReadPageMargin(PageMargin? pageMargin)
    {
        if (pageMargin is null)
        {
            return null;
        }

        return new PageMarginInfo
        {
            TopTwips = ToInt(pageMargin.Top),
            RightTwips = ToInt(pageMargin.Right),
            BottomTwips = ToInt(pageMargin.Bottom),
            LeftTwips = ToInt(pageMargin.Left),
            HeaderTwips = ToInt(pageMargin.Header),
            FooterTwips = ToInt(pageMargin.Footer),
            GutterTwips = ToInt(pageMargin.Gutter)
        };
    }

    private static List<HeaderFooterReference> ReadHeaders(SectionProperties section)
    {
        return section
            .Elements<HeaderReference>()
            .Select(header => new HeaderFooterReference
            {
                Type = LowerInnerText(header.Type),
                RelationshipId = header.Id?.Value
            })
            .ToList();
    }

    private static List<HeaderFooterReference> ReadFooters(SectionProperties section)
    {
        return section
            .Elements<FooterReference>()
            .Select(footer => new HeaderFooterReference
            {
                Type = LowerInnerText(footer.Type),
                RelationshipId = footer.Id?.Value
            })
            .ToList();
    }

    private static List<DocumentTable> ReadTables(Body body)
    {
        return body
            .Descendants<Table>()
            .Select((table, index) =>
            {
                var rows = table.Elements<TableRow>().ToList();

                return new DocumentTable
                {
                    Index = index,
                    RowCount = rows.Count,
                    CellCounts = rows
                        .Select(row => row.Elements<TableCell>().Count())
                        .ToList(),
                    TextPreview = Preview(string.Join(" ", rows.SelectMany(row =>
                        row.Elements<TableCell>().Select(cell => cell.InnerText))))
                };
            })
            .ToList();
    }

    private static List<string> GetFinalizationReasons(Body body)
    {
        var reasons = new List<string>();

        if (body.Descendants<FieldChar>().Any()
            || body.Descendants<FieldCode>().Any()
            || body.Descendants<SimpleField>().Any())
        {
            reasons.Add("fields");
        }

        return reasons;
    }

    private static bool IsFieldOnlyParagraph(Paragraph paragraph)
    {
        var hasFields = paragraph.Descendants<FieldChar>().Any()
            || paragraph.Descendants<FieldCode>().Any()
            || paragraph.Descendants<SimpleField>().Any();
        return hasFields && !paragraph.Descendants<Text>().Any(text => !string.IsNullOrWhiteSpace(text.Text));
    }

    private static string? LowerInnerText(OpenXmlSimpleType? value)
    {
        return string.IsNullOrWhiteSpace(value?.InnerText)
            ? null
            : value.InnerText.ToLowerInvariant();
    }

    private static bool IsExpectedInspectionFailure(Exception ex)
    {
        return ex is InvalidDataException
            or FileFormatException
            or OpenXmlPackageException
            or IOException
            or UnauthorizedAccessException;
    }

    private static int? ToInt(UInt32Value? value)
    {
        return value?.Value is null ? null : checked((int)value.Value);
    }

    private static int? ToInt(Int32Value? value)
    {
        return value?.Value;
    }

    private static string Preview(string text)
    {
        if (text.Length <= PreviewLimit)
        {
            return text;
        }

        return text[..PreviewLimit];
    }
}
