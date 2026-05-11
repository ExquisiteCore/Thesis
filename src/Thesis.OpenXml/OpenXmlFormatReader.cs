using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlFormatReader
{
    private const int PreviewLimit = 200;
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static ParagraphFormatSample ReadParagraphFormat(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var runProperties = paragraph.Descendants<Run>().FirstOrDefault(run => run.Descendants<Text>().Any())?.RunProperties;
        return new ParagraphFormatSample
        {
            StyleId = properties?.ParagraphStyleId?.Val?.Value,
            Alignment = properties?.Justification?.Val?.InnerText,
            SpacingBeforeTwips = ToInt(properties?.SpacingBetweenLines?.Before),
            SpacingAfterTwips = ToInt(properties?.SpacingBetweenLines?.After),
            LineSpacing = properties?.SpacingBetweenLines?.Line?.Value,
            LineSpacingRule = properties?.SpacingBetweenLines?.LineRule?.InnerText,
            FirstLineIndentTwips = ToInt(properties?.Indentation?.FirstLine),
            LeftIndentTwips = ToInt(properties?.Indentation?.Left),
            RightIndentTwips = ToInt(properties?.Indentation?.Right),
            RunFormat = runProperties is null
                ? null
                : new RunFormatSample
                {
                    Bold = ReadOnOffValue(runProperties.Bold),
                    Italic = ReadOnOffValue(runProperties.Italic),
                    FontSizeHalfPoints = runProperties.FontSize?.Val?.Value,
                    AsciiFont = runProperties.RunFonts?.Ascii?.Value,
                    HighAnsiFont = runProperties.RunFonts?.HighAnsi?.Value,
                    EastAsiaFont = runProperties.RunFonts?.EastAsia?.Value,
                    ComplexScriptFont = runProperties.RunFonts?.ComplexScript?.Value
                }
        };
    }

    public static TableFormatSample ReadTableFormat(Table table)
    {
        var properties = table.TableProperties;
        var width = properties?.TableWidth;
        var firstCellParagraph = table
            .Elements<TableRow>()
            .SelectMany(row => row.Elements<TableCell>())
            .SelectMany(cell => cell.Elements<Paragraph>())
            .FirstOrDefault();

        return new TableFormatSample
        {
            WidthTwips = ToInt(GetWordprocessingAttribute(width, "w")),
            WidthType = GetWordprocessingAttribute(width, "type"),
            Alignment = properties?.TableJustification?.Val?.InnerText,
            GridColumnWidthsTwips = [.. table.TableGrid?
                .Elements<GridColumn>()
                .Select(column => ToInt(column.Width))
                .OfType<int>() ?? []],
            Borders = ReadTableBorders(properties?.TableBorders),
            CellMargins = ReadTableCellMargins(properties?.TableCellMarginDefault),
            HeaderRowCount = table.Elements<TableRow>().Count(row => row.TableRowProperties?.GetFirstChild<TableHeader>() is not null),
            FirstCellParagraphFormat = firstCellParagraph is null ? null : ReadParagraphFormat(firstCellParagraph)
        };
    }

    public static TableBordersSample? ReadTableBorders(TableBorders? borders)
    {
        if (borders is null)
        {
            return null;
        }

        return new TableBordersSample
        {
            Top = ReadBorderLine(borders.TopBorder),
            Bottom = ReadBorderLine(borders.BottomBorder),
            Left = ReadBorderLine(borders.LeftBorder ?? (OpenXmlElement?)borders.StartBorder),
            Right = ReadBorderLine(borders.RightBorder ?? (OpenXmlElement?)borders.EndBorder),
            InsideHorizontal = ReadBorderLine(borders.InsideHorizontalBorder),
            InsideVertical = ReadBorderLine(borders.InsideVerticalBorder)
        };
    }

    public static TableBorderLineSample? ReadBorderLine(OpenXmlElement? border)
    {
        if (border is null)
        {
            return null;
        }

        return new TableBorderLineSample
        {
            Value = GetWordprocessingAttribute(border, "val"),
            Size = GetWordprocessingAttribute(border, "sz"),
            Color = GetWordprocessingAttribute(border, "color"),
            Space = GetWordprocessingAttribute(border, "space")
        };
    }

    public static TableCellMarginsSample? ReadTableCellMargins(TableCellMarginDefault? margins)
    {
        if (margins is null)
        {
            return null;
        }

        return new TableCellMarginsSample
        {
            TopTwips = ToInt(GetWordprocessingAttribute(margins.TopMargin, "w")),
            RightTwips = ToInt(GetWordprocessingAttribute(margins.TableCellRightMargin, "w")
                ?? GetWordprocessingAttribute(margins.EndMargin, "w")),
            BottomTwips = ToInt(GetWordprocessingAttribute(margins.BottomMargin, "w")),
            LeftTwips = ToInt(GetWordprocessingAttribute(margins.TableCellLeftMargin, "w")
                ?? GetWordprocessingAttribute(margins.StartMargin, "w"))
        };
    }

    public static ParagraphFormatSample ReadFirstCellParagraphFormat(TableCell cell)
    {
        var paragraph = cell.Elements<Paragraph>().FirstOrDefault();
        return paragraph is null ? new ParagraphFormatSample() : ReadParagraphFormat(paragraph);
    }

    public static string RunPreview(Run run)
    {
        var properties = run.RunProperties;
        return $"text={Preview(run.InnerText)};bold={properties?.Bold is not null};italic={properties?.Italic is not null};fontSizeHalfPoints={properties?.FontSize?.Val?.Value}";
    }

    public static string FormatPreview(JsonNode? format)
    {
        return format?.ToJsonString(ThesisJson.Options) ?? "{}";
    }

    public static string FormatPreview(ParagraphFormatSample format)
    {
        return ThesisJson.Serialize(format);
    }

    public static string FormatPreview(TableFormatSample format)
    {
        return ThesisJson.Serialize(format);
    }

    public static string ParagraphFormatPreview(Paragraph paragraph)
    {
        return FormatPreview(ReadParagraphFormat(paragraph));
    }

    public static string TableFormatPreview(Table table)
    {
        return FormatPreview(ReadTableFormat(table));
    }

    public static string CellFormatPreview(TableCell cell)
    {
        return FormatPreview(ReadFirstCellParagraphFormat(cell));
    }

    public static string? GetWordprocessingAttribute(OpenXmlElement? element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (string.Equals(attribute.LocalName, localName, StringComparison.Ordinal)
                && string.Equals(attribute.NamespaceUri, WordprocessingNamespace, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attribute.Value) ? null : attribute.Value;
            }
        }

        return null;
    }

    private static bool? ReadOnOffValue(OnOffType? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Val is null)
        {
            return true;
        }

        return value.Val.Value;
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int? ToInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string Preview(string text)
    {
        return text.Length <= PreviewLimit ? text : text[..PreviewLimit];
    }
}
