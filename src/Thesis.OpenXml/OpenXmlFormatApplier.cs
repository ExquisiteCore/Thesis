using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlFormatApplier
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void ApplyParagraphFormat(Paragraph paragraph, ParagraphFormatSample format)
    {
        var properties = GetOrCreateParagraphProperties(paragraph);

        if (!string.IsNullOrWhiteSpace(format.StyleId))
        {
            var paragraphStyle = properties.ParagraphStyleId;
            if (paragraphStyle is null)
            {
                paragraphStyle = new ParagraphStyleId();
                properties.PrependChild(paragraphStyle);
            }

            paragraphStyle.Val = format.StyleId;
        }

        if (!string.IsNullOrWhiteSpace(format.Alignment))
        {
            properties.Justification ??= new Justification();
            SetWordprocessingAttribute(properties.Justification, "val", format.Alignment);
        }

        if (format.SpacingBeforeTwips is not null
            || format.SpacingAfterTwips is not null
            || format.LineSpacing is not null
            || format.LineSpacingRule is not null)
        {
            properties.SpacingBetweenLines ??= new SpacingBetweenLines();
            if (format.SpacingBeforeTwips is not null)
            {
                properties.SpacingBetweenLines.Before = format.SpacingBeforeTwips.Value.ToString();
            }

            if (format.SpacingAfterTwips is not null)
            {
                properties.SpacingBetweenLines.After = format.SpacingAfterTwips.Value.ToString();
            }

            if (format.LineSpacing is not null)
            {
                properties.SpacingBetweenLines.Line = format.LineSpacing;
            }

            if (format.LineSpacingRule is not null)
            {
                SetWordprocessingAttribute(properties.SpacingBetweenLines, "lineRule", format.LineSpacingRule);
            }
        }

        if (format.FirstLineIndentTwips is not null
            || format.LeftIndentTwips is not null
            || format.RightIndentTwips is not null)
        {
            properties.Indentation ??= new Indentation();
            if (format.FirstLineIndentTwips is not null)
            {
                properties.Indentation.FirstLine = format.FirstLineIndentTwips.Value.ToString();
            }

            if (format.LeftIndentTwips is not null)
            {
                properties.Indentation.Left = format.LeftIndentTwips.Value.ToString();
            }

            if (format.RightIndentTwips is not null)
            {
                properties.Indentation.Right = format.RightIndentTwips.Value.ToString();
            }
        }

        if (format.RunFormat is not null)
        {
            ApplyRunFormat(paragraph, format.RunFormat);
        }
    }

    public static void ApplyRunFormat(Paragraph paragraph, RunFormatSample format)
    {
        foreach (var run in paragraph.Descendants<Run>().Where(run => run.Descendants<Text>().Any()))
        {
            var properties = GetOrCreateRunProperties(run);

            if (format.AsciiFont is not null
                || format.HighAnsiFont is not null
                || format.EastAsiaFont is not null
                || format.ComplexScriptFont is not null)
            {
                properties.RunFonts ??= new RunFonts();
                if (format.AsciiFont is not null)
                {
                    properties.RunFonts.Ascii = format.AsciiFont;
                }

                if (format.HighAnsiFont is not null)
                {
                    properties.RunFonts.HighAnsi = format.HighAnsiFont;
                }

                if (format.EastAsiaFont is not null)
                {
                    properties.RunFonts.EastAsia = format.EastAsiaFont;
                }

                if (format.ComplexScriptFont is not null)
                {
                    properties.RunFonts.ComplexScript = format.ComplexScriptFont;
                }
            }

            if (format.Bold is not null)
            {
                properties.Bold ??= new Bold();
                properties.Bold.Val = format.Bold.Value;
            }

            if (format.Italic is not null)
            {
                properties.Italic ??= new Italic();
                properties.Italic.Val = format.Italic.Value;
            }

            if (format.FontSizeHalfPoints is not null)
            {
                properties.FontSize ??= new FontSize();
                properties.FontSize.Val = format.FontSizeHalfPoints;
            }
        }
    }

    public static void ApplyTableFormat(Table table, TableFormatSample format)
    {
        var properties = GetOrCreateTableProperties(table);

        if (format.WidthTwips is not null || format.WidthType is not null)
        {
            properties.TableWidth ??= new TableWidth();
            if (format.WidthTwips is not null)
            {
                SetWordprocessingAttribute(properties.TableWidth, "w", format.WidthTwips.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(format.WidthType))
            {
                SetWordprocessingAttribute(properties.TableWidth, "type", format.WidthType);
            }
        }

        if (!string.IsNullOrWhiteSpace(format.Alignment))
        {
            properties.TableJustification ??= new TableJustification();
            SetWordprocessingAttribute(properties.TableJustification, "val", format.Alignment);
        }

        if (format.Borders is not null)
        {
            ApplyTableBorders(properties, format.Borders);
        }

        if (format.CellMargins is not null)
        {
            ApplyTableCellMargins(properties, format.CellMargins);
        }

        if (format.GridColumnWidthsTwips.Count > 0)
        {
            ApplyTableGrid(table, format.GridColumnWidthsTwips);
        }

        ApplyTableHeaderRows(table, format.HeaderRowCount);

        if (format.FirstCellParagraphFormat is not null)
        {
            var firstCellParagraph = table
                .Elements<TableRow>()
                .SelectMany(row => row.Elements<TableCell>())
                .SelectMany(cell => cell.Elements<Paragraph>())
                .FirstOrDefault();
            if (firstCellParagraph is not null)
            {
                ApplyParagraphFormat(firstCellParagraph, format.FirstCellParagraphFormat);
            }
        }
    }

    public static void ApplyTableBorders(TableProperties properties, TableBordersSample borders)
    {
        var merged = OpenXmlFormatMerger.MergeTableBorders(OpenXmlFormatReader.ReadTableBorders(properties.TableBorders), borders);
        properties.TableBorders?.Remove();
        if (merged is null)
        {
            return;
        }

        var tableBorders = new TableBorders();
        ApplyBorderLine(tableBorders, merged.Top, () => new TopBorder());
        ApplyBorderLine(tableBorders, merged.Left, () => new LeftBorder());
        ApplyBorderLine(tableBorders, merged.Bottom, () => new BottomBorder());
        ApplyBorderLine(tableBorders, merged.Right, () => new RightBorder());
        ApplyBorderLine(tableBorders, merged.InsideHorizontal, () => new InsideHorizontalBorder());
        ApplyBorderLine(tableBorders, merged.InsideVertical, () => new InsideVerticalBorder());
        properties.TableBorders = tableBorders;
    }

    public static void ApplyBorderLine<T>(TableBorders borders, TableBorderLineSample? sample, Func<T> create)
        where T : OpenXmlElement
    {
        if (sample is null)
        {
            return;
        }

        var existing = borders.Elements<T>().FirstOrDefault();
        if (existing is null)
        {
            existing = create();
            borders.AppendChild(existing);
        }

        if (!string.IsNullOrWhiteSpace(sample.Value))
        {
            SetWordprocessingAttribute(existing, "val", sample.Value);
        }

        if (!string.IsNullOrWhiteSpace(sample.Size))
        {
            SetWordprocessingAttribute(existing, "sz", sample.Size);
        }

        if (!string.IsNullOrWhiteSpace(sample.Color))
        {
            SetWordprocessingAttribute(existing, "color", sample.Color);
        }

        if (!string.IsNullOrWhiteSpace(sample.Space))
        {
            SetWordprocessingAttribute(existing, "space", sample.Space);
        }
    }

    public static void ApplyTableCellMargins(TableProperties properties, TableCellMarginsSample margins)
    {
        var existing = properties.TableCellMarginDefault;
        existing?.Remove();
        var marginDefault = new TableCellMarginDefault();
        if (margins.TopTwips is not null)
        {
            var top = new TopMargin();
            SetWordprocessingAttribute(top, "w", margins.TopTwips.Value.ToString());
            SetWordprocessingAttribute(top, "type", "dxa");
            marginDefault.AppendChild(top);
        }

        if (margins.LeftTwips is not null)
        {
            var left = new TableCellLeftMargin();
            SetWordprocessingAttribute(left, "w", margins.LeftTwips.Value.ToString());
            SetWordprocessingAttribute(left, "type", "dxa");
            marginDefault.AppendChild(left);
        }

        if (margins.BottomTwips is not null)
        {
            var bottom = new BottomMargin();
            SetWordprocessingAttribute(bottom, "w", margins.BottomTwips.Value.ToString());
            SetWordprocessingAttribute(bottom, "type", "dxa");
            marginDefault.AppendChild(bottom);
        }

        if (margins.RightTwips is not null)
        {
            var right = new TableCellRightMargin();
            SetWordprocessingAttribute(right, "w", margins.RightTwips.Value.ToString());
            SetWordprocessingAttribute(right, "type", "dxa");
            marginDefault.AppendChild(right);
        }

        properties.TableCellMarginDefault = marginDefault;
    }

    public static void ApplyTableGrid(Table table, List<int> widths)
    {
        table.TableGrid?.Remove();
        var grid = new TableGrid();
        foreach (var width in widths)
        {
            var column = new GridColumn();
            SetWordprocessingAttribute(column, "w", width.ToString());
            grid.AppendChild(column);
        }

        var properties = table.TableProperties;
        if (properties is not null)
        {
            table.InsertAfter(grid, properties);
        }
        else
        {
            table.PrependChild(grid);
        }
    }

    public static void EnsureTableGrid(Table table)
    {
        if (table.TableGrid is not null)
        {
            return;
        }

        var columnCount = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Count())
            .DefaultIfEmpty(0)
            .Max();
        if (columnCount == 0)
        {
            return;
        }

        ApplyTableGrid(table, Enumerable.Repeat(0, columnCount).ToList());
    }

    public static void ApplyTableHeaderRows(Table table, int headerRowCount)
    {
        var rows = table.Elements<TableRow>().ToList();
        for (var index = 0; index < rows.Count; index++)
        {
            var properties = GetOrCreateTableRowProperties(rows[index]);
            var existing = properties.GetFirstChild<TableHeader>();
            if (index < headerRowCount)
            {
                if (existing is null)
                {
                    properties.AppendChild(new TableHeader());
                }
            }
            else
            {
                existing?.Remove();
            }
        }
    }

    public static void ReplaceTableCellText(TableCell cell, string text)
    {
        var cellProperties = cell.TableCellProperties?.CloneNode(deep: true) as TableCellProperties;
        cell.RemoveAllChildren();
        if (cellProperties is not null)
        {
            cell.AppendChild(cellProperties);
        }

        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));
        cell.AppendChild(paragraph);
    }

    public static void ApplyTableCellFormat(TableCell cell, ParagraphFormatSample format)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();
        if (paragraphs.Count == 0)
        {
            var paragraph = new Paragraph(new Run(new Text("")));
            cell.AppendChild(paragraph);
            paragraphs.Add(paragraph);
        }

        foreach (var paragraph in paragraphs)
        {
            ApplyParagraphFormat(paragraph, format);
        }
    }

    public static void ApplyTableColumnWidth(Table table, int columnIndex, int widthTwips)
    {
        ApplyTableGrid(table, GetMergedGridWidths(table, columnIndex, widthTwips));

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (columnIndex >= cells.Count)
            {
                continue;
            }

            var properties = GetOrCreateTableCellProperties(cells[columnIndex]);
            properties.TableCellWidth ??= new TableCellWidth();
            SetWordprocessingAttribute(properties.TableCellWidth, "w", widthTwips.ToString());
            SetWordprocessingAttribute(properties.TableCellWidth, "type", "dxa");
        }
    }

    public static List<int> GetMergedGridWidths(Table table, int columnIndex, int widthTwips)
    {
        var existing = table.TableGrid?
            .Elements<GridColumn>()
            .Select(column => ToInt(column.Width) ?? 0)
            .ToList() ?? [];
        var columnCount = Math.Max(
            columnIndex + 1,
            table.Elements<TableRow>()
                .Select(row => row.Elements<TableCell>().Count())
                .DefaultIfEmpty(0)
                .Max());

        while (existing.Count < columnCount)
        {
            existing.Add(0);
        }

        existing[columnIndex] = widthTwips;
        return existing;
    }

    public static void SetTableRowHeader(Table table, int rowIndex, bool header)
    {
        var row = table.Elements<TableRow>().ElementAt(rowIndex);
        var properties = GetOrCreateTableRowProperties(row);
        var existing = properties.GetFirstChild<TableHeader>();
        if (header)
        {
            if (existing is null)
            {
                properties.AppendChild(new TableHeader());
            }
        }
        else
        {
            existing?.Remove();
        }
    }

    public static TableProperties GetOrCreateTableProperties(Table table)
    {
        if (table.TableProperties is not null)
        {
            return table.TableProperties;
        }

        var properties = new TableProperties();
        table.PrependChild(properties);
        return properties;
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

    private static RunProperties GetOrCreateRunProperties(Run run)
    {
        if (run.RunProperties is not null)
        {
            return run.RunProperties;
        }

        var properties = new RunProperties();
        run.PrependChild(properties);
        return properties;
    }

    private static TableRowProperties GetOrCreateTableRowProperties(TableRow row)
    {
        if (row.TableRowProperties is not null)
        {
            return row.TableRowProperties;
        }

        var properties = new TableRowProperties();
        row.PrependChild(properties);
        return properties;
    }

    private static TableCellProperties GetOrCreateTableCellProperties(TableCell cell)
    {
        if (cell.TableCellProperties is not null)
        {
            return cell.TableCellProperties;
        }

        var properties = new TableCellProperties();
        cell.PrependChild(properties);
        return properties;
    }

    private static void SetWordprocessingAttribute(OpenXmlElement element, string localName, string value)
    {
        element.SetAttribute(new OpenXmlAttribute("w", localName, WordprocessingNamespace, value));
    }

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }
}
