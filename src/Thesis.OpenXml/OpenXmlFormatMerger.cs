using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlFormatMerger
{
    public static ParagraphFormatSample Clone(ParagraphFormatSample value)
    {
        return new ParagraphFormatSample
        {
            StyleId = value.StyleId,
            Alignment = value.Alignment,
            SpacingBeforeTwips = value.SpacingBeforeTwips,
            SpacingAfterTwips = value.SpacingAfterTwips,
            LineSpacing = value.LineSpacing,
            LineSpacingRule = value.LineSpacingRule,
            FirstLineIndentTwips = value.FirstLineIndentTwips,
            LeftIndentTwips = value.LeftIndentTwips,
            RightIndentTwips = value.RightIndentTwips,
            RunFormat = Clone(value.RunFormat)
        };
    }

    public static RunFormatSample? Clone(RunFormatSample? value)
    {
        return value is null
            ? null
            : new RunFormatSample
            {
                Bold = value.Bold,
                Italic = value.Italic,
                FontSizeHalfPoints = value.FontSizeHalfPoints,
                AsciiFont = value.AsciiFont,
                HighAnsiFont = value.HighAnsiFont,
                EastAsiaFont = value.EastAsiaFont,
                ComplexScriptFont = value.ComplexScriptFont
            };
    }

    public static TableFormatSample Clone(TableFormatSample value)
    {
        return new TableFormatSample
        {
            WidthTwips = value.WidthTwips,
            WidthType = value.WidthType,
            Alignment = value.Alignment,
            GridColumnWidthsTwips = [.. value.GridColumnWidthsTwips],
            Borders = Clone(value.Borders),
            CellMargins = Clone(value.CellMargins),
            HeaderRowCount = value.HeaderRowCount,
            FirstCellParagraphFormat = value.FirstCellParagraphFormat is null
                ? null
                : Clone(value.FirstCellParagraphFormat)
        };
    }

    public static TableBordersSample? Clone(TableBordersSample? value)
    {
        return value is null
            ? null
            : new TableBordersSample
            {
                Top = Clone(value.Top),
                Bottom = Clone(value.Bottom),
                Left = Clone(value.Left),
                Right = Clone(value.Right),
                InsideHorizontal = Clone(value.InsideHorizontal),
                InsideVertical = Clone(value.InsideVertical)
            };
    }

    public static TableBorderLineSample? Clone(TableBorderLineSample? value)
    {
        return value is null
            ? null
            : new TableBorderLineSample
            {
                Value = value.Value,
                Size = value.Size,
                Color = value.Color,
                Space = value.Space
            };
    }

    public static TableCellMarginsSample? Clone(TableCellMarginsSample? value)
    {
        return value is null
            ? null
            : new TableCellMarginsSample
            {
                TopTwips = value.TopTwips,
                RightTwips = value.RightTwips,
                BottomTwips = value.BottomTwips,
                LeftTwips = value.LeftTwips
            };
    }

    public static ParagraphFormatSample MergeParagraphFormat(ParagraphFormatSample current, ParagraphFormatSample delta)
    {
        var merged = Clone(current);
        merged.StyleId = delta.StyleId ?? merged.StyleId;
        merged.Alignment = delta.Alignment ?? merged.Alignment;
        merged.SpacingBeforeTwips = delta.SpacingBeforeTwips ?? merged.SpacingBeforeTwips;
        merged.SpacingAfterTwips = delta.SpacingAfterTwips ?? merged.SpacingAfterTwips;
        merged.LineSpacing = delta.LineSpacing ?? merged.LineSpacing;
        merged.LineSpacingRule = delta.LineSpacingRule ?? merged.LineSpacingRule;
        merged.FirstLineIndentTwips = delta.FirstLineIndentTwips ?? merged.FirstLineIndentTwips;
        merged.LeftIndentTwips = delta.LeftIndentTwips ?? merged.LeftIndentTwips;
        merged.RightIndentTwips = delta.RightIndentTwips ?? merged.RightIndentTwips;
        merged.RunFormat = MergeRunFormat(merged.RunFormat, delta.RunFormat);
        return merged;
    }

    public static RunFormatSample? MergeRunFormat(RunFormatSample? current, RunFormatSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new RunFormatSample();
        merged.Bold = delta.Bold ?? merged.Bold;
        merged.Italic = delta.Italic ?? merged.Italic;
        merged.FontSizeHalfPoints = delta.FontSizeHalfPoints ?? merged.FontSizeHalfPoints;
        merged.AsciiFont = delta.AsciiFont ?? merged.AsciiFont;
        merged.HighAnsiFont = delta.HighAnsiFont ?? merged.HighAnsiFont;
        merged.EastAsiaFont = delta.EastAsiaFont ?? merged.EastAsiaFont;
        merged.ComplexScriptFont = delta.ComplexScriptFont ?? merged.ComplexScriptFont;
        return merged;
    }

    public static TableFormatSample MergeTableFormat(TableFormatSample current, TableFormatSample delta)
    {
        var merged = Clone(current);
        merged.WidthTwips = delta.WidthTwips ?? merged.WidthTwips;
        merged.WidthType = delta.WidthType ?? merged.WidthType;
        merged.Alignment = delta.Alignment ?? merged.Alignment;
        if (delta.GridColumnWidthsTwips.Count > 0)
        {
            merged.GridColumnWidthsTwips = [.. delta.GridColumnWidthsTwips];
        }

        merged.Borders = MergeTableBorders(merged.Borders, delta.Borders);
        merged.CellMargins = MergeTableCellMargins(merged.CellMargins, delta.CellMargins);
        merged.HeaderRowCount = delta.HeaderRowCount;
        merged.FirstCellParagraphFormat = delta.FirstCellParagraphFormat is null
            ? merged.FirstCellParagraphFormat is null ? null : Clone(merged.FirstCellParagraphFormat)
            : MergeParagraphFormat(merged.FirstCellParagraphFormat ?? new ParagraphFormatSample(), delta.FirstCellParagraphFormat);
        return merged;
    }

    public static TableBordersSample? MergeTableBorders(TableBordersSample? current, TableBordersSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableBordersSample();
        merged.Top = MergeBorderLine(merged.Top, delta.Top);
        merged.Bottom = MergeBorderLine(merged.Bottom, delta.Bottom);
        merged.Left = MergeBorderLine(merged.Left, delta.Left);
        merged.Right = MergeBorderLine(merged.Right, delta.Right);
        merged.InsideHorizontal = MergeBorderLine(merged.InsideHorizontal, delta.InsideHorizontal);
        merged.InsideVertical = MergeBorderLine(merged.InsideVertical, delta.InsideVertical);
        return merged;
    }

    public static TableBorderLineSample? MergeBorderLine(TableBorderLineSample? current, TableBorderLineSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableBorderLineSample();
        merged.Value = delta.Value ?? merged.Value;
        merged.Size = delta.Size ?? merged.Size;
        merged.Color = delta.Color ?? merged.Color;
        merged.Space = delta.Space ?? merged.Space;
        return merged;
    }

    public static TableCellMarginsSample? MergeTableCellMargins(TableCellMarginsSample? current, TableCellMarginsSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableCellMarginsSample();
        merged.TopTwips = delta.TopTwips ?? merged.TopTwips;
        merged.RightTwips = delta.RightTwips ?? merged.RightTwips;
        merged.BottomTwips = delta.BottomTwips ?? merged.BottomTwips;
        merged.LeftTwips = delta.LeftTwips ?? merged.LeftTwips;
        return merged;
    }
}
