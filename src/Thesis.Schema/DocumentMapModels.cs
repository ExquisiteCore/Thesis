namespace Thesis.Schema;

public sealed class DocumentMap
{
    public string SchemaVersion { get; set; } = "1.0";

    public string Path { get; set; } = "";

    public bool RequiresFinalization { get; set; }

    public List<string> FinalizationReasons { get; set; } = [];

    public List<DocumentParagraph> Paragraphs { get; set; } = [];

    public List<DocumentStyle> Styles { get; set; } = [];

    public List<DocumentNumbering> Numbering { get; set; } = [];

    public List<DocumentSection> Sections { get; set; } = [];

    public List<DocumentTable> Tables { get; set; } = [];
}

public sealed class DocumentParagraph
{
    public int Index { get; set; }

    public string Text { get; set; } = "";

    public string? StyleId { get; set; }

    public int? OutlineLevel { get; set; }

    public ParagraphFormatSample Format { get; set; } = new();

    public NumberingReference? Numbering { get; set; }

    public List<DocumentRun> Runs { get; set; } = [];
}

public sealed class ParagraphFormatSample
{
    public string? StyleId { get; set; }

    public string? Alignment { get; set; }

    public int? SpacingBeforeTwips { get; set; }

    public int? SpacingAfterTwips { get; set; }

    public string? LineSpacing { get; set; }

    public string? LineSpacingRule { get; set; }

    public int? FirstLineIndentTwips { get; set; }

    public int? LeftIndentTwips { get; set; }

    public int? RightIndentTwips { get; set; }

    public RunFormatSample? RunFormat { get; set; }
}

public sealed class RunFormatSample
{
    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public string? FontSizeHalfPoints { get; set; }

    public string? AsciiFont { get; set; }

    public string? HighAnsiFont { get; set; }

    public string? EastAsiaFont { get; set; }

    public string? ComplexScriptFont { get; set; }
}

public sealed class DocumentRun
{
    public int Index { get; set; }

    public string Text { get; set; } = "";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string? FontSizeHalfPoints { get; set; }

    public string? AsciiFont { get; set; }

    public string? HighAnsiFont { get; set; }

    public string? EastAsiaFont { get; set; }

    public string? ComplexScriptFont { get; set; }
}

public sealed class NumberingReference
{
    public string? NumberingId { get; set; }

    public string? Level { get; set; }
}

public sealed class DocumentStyle
{
    public string? StyleId { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? BasedOn { get; set; }

    public int UsageCount { get; set; }
}

public sealed class DocumentNumbering
{
    public string? NumberingId { get; set; }

    public string? AbstractNumberingId { get; set; }

    public List<DocumentNumberingLevel> Levels { get; set; } = [];
}

public sealed class DocumentNumberingLevel
{
    public string? Level { get; set; }

    public string? Format { get; set; }

    public string? Text { get; set; }
}

public sealed class DocumentSection
{
    public int Index { get; set; }

    public PageSizeInfo? PageSize { get; set; }

    public PageMarginInfo? PageMargin { get; set; }

    public List<HeaderFooterReference> Headers { get; set; } = [];

    public List<HeaderFooterReference> Footers { get; set; } = [];
}

public sealed class PageSizeInfo
{
    public int? WidthTwips { get; set; }

    public int? HeightTwips { get; set; }

    public string? Orientation { get; set; }
}

public sealed class PageMarginInfo
{
    public int? TopTwips { get; set; }

    public int? RightTwips { get; set; }

    public int? BottomTwips { get; set; }

    public int? LeftTwips { get; set; }

    public int? HeaderTwips { get; set; }

    public int? FooterTwips { get; set; }

    public int? GutterTwips { get; set; }
}

public sealed class HeaderFooterReference
{
    public string? Type { get; set; }

    public string? RelationshipId { get; set; }
}

public sealed class DocumentTable
{
    public int Index { get; set; }

    public int RowCount { get; set; }

    public List<int> CellCounts { get; set; } = [];

    public string TextPreview { get; set; } = "";

    public TableFormatSample Format { get; set; } = new();
}

public sealed class TableFormatSample
{
    public int? WidthTwips { get; set; }

    public string? WidthType { get; set; }

    public string? Alignment { get; set; }

    public List<int> GridColumnWidthsTwips { get; set; } = [];

    public TableBordersSample? Borders { get; set; }

    public TableCellMarginsSample? CellMargins { get; set; }

    public int HeaderRowCount { get; set; }

    public ParagraphFormatSample? FirstCellParagraphFormat { get; set; }
}

public sealed class TableBordersSample
{
    public TableBorderLineSample? Top { get; set; }

    public TableBorderLineSample? Bottom { get; set; }

    public TableBorderLineSample? Left { get; set; }

    public TableBorderLineSample? Right { get; set; }

    public TableBorderLineSample? InsideHorizontal { get; set; }

    public TableBorderLineSample? InsideVertical { get; set; }
}

public sealed class TableBorderLineSample
{
    public string? Value { get; set; }

    public string? Size { get; set; }

    public string? Color { get; set; }

    public string? Space { get; set; }
}

public sealed class TableCellMarginsSample
{
    public int? TopTwips { get; set; }

    public int? RightTwips { get; set; }

    public int? BottomTwips { get; set; }

    public int? LeftTwips { get; set; }
}

