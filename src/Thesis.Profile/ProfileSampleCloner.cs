using Thesis.Schema;

namespace Thesis.Profile;

internal static class ProfileSampleCloner
{
    public static PageSizeInfo Clone(PageSizeInfo value)
    {
        return new PageSizeInfo
        {
            WidthTwips = value.WidthTwips,
            HeightTwips = value.HeightTwips,
            Orientation = value.Orientation
        };
    }

    public static PageMarginInfo Clone(PageMarginInfo value)
    {
        return new PageMarginInfo
        {
            TopTwips = value.TopTwips,
            RightTwips = value.RightTwips,
            BottomTwips = value.BottomTwips,
            LeftTwips = value.LeftTwips,
            HeaderTwips = value.HeaderTwips,
            FooterTwips = value.FooterTwips,
            GutterTwips = value.GutterTwips
        };
    }

    public static ParagraphFormatSample? Clone(ParagraphFormatSample? value)
    {
        return value is null
            ? null
            : new ParagraphFormatSample
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

    public static TableFormatSample? Clone(TableFormatSample? value)
    {
        return value is null
            ? null
            : new TableFormatSample
            {
                WidthTwips = value.WidthTwips,
                WidthType = value.WidthType,
                Alignment = value.Alignment,
                GridColumnWidthsTwips = [.. value.GridColumnWidthsTwips],
                Borders = Clone(value.Borders),
                CellMargins = Clone(value.CellMargins),
                HeaderRowCount = value.HeaderRowCount,
                FirstCellParagraphFormat = Clone(value.FirstCellParagraphFormat)
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

    public static HeaderFooterReference Clone(HeaderFooterReference value)
    {
        return new HeaderFooterReference
        {
            Type = value.Type,
            RelationshipId = value.RelationshipId
        };
    }

    public static DocumentNumbering Clone(DocumentNumbering value)
    {
        return new DocumentNumbering
        {
            NumberingId = value.NumberingId,
            AbstractNumberingId = value.AbstractNumberingId,
            Levels = [.. value.Levels.Select(level => new DocumentNumberingLevel
            {
                Level = level.Level,
                Format = level.Format,
                Text = level.Text
            })]
        };
    }
}
