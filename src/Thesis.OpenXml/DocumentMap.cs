namespace Thesis.OpenXml;

public sealed class DocumentMap
{
    public string SchemaVersion { get; init; } = "1.0";

    public string Path { get; init; } = "";

    public bool RequiresFinalization { get; init; }

    public IReadOnlyList<string> FinalizationReasons { get; init; } = [];

    public IReadOnlyList<ParagraphMap> Paragraphs { get; init; } = [];

    public IReadOnlyList<StyleMap> Styles { get; init; } = [];

    public IReadOnlyList<NumberingMap> Numbering { get; init; } = [];

    public IReadOnlyList<SectionMap> Sections { get; init; } = [];

    public IReadOnlyList<TableMap> Tables { get; init; } = [];
}

public sealed class ParagraphMap
{
    public int Index { get; init; }

    public string Text { get; init; } = "";

    public string? StyleId { get; init; }

    public ParagraphNumberingMap? Numbering { get; init; }

    public IReadOnlyList<RunSummaryMap> Runs { get; init; } = [];
}

public sealed class ParagraphNumberingMap
{
    public string? NumberingId { get; init; }

    public string? Level { get; init; }
}

public sealed class RunSummaryMap
{
    public int Index { get; init; }

    public string Text { get; init; } = "";

    public string? StyleId { get; init; }

    public bool IsBold { get; init; }

    public bool IsItalic { get; init; }
}

public sealed class StyleMap
{
    public string? StyleId { get; init; }

    public string? Name { get; init; }

    public string? Type { get; init; }

    public string? BasedOn { get; init; }
}

public sealed class NumberingMap
{
    public string? NumberingId { get; init; }

    public string? AbstractNumberingId { get; init; }

    public IReadOnlyList<NumberingLevelMap> Levels { get; init; } = [];
}

public sealed class NumberingLevelMap
{
    public string? Level { get; init; }

    public string? Format { get; init; }

    public string? Text { get; init; }
}

public sealed class SectionMap
{
    public int Index { get; init; }

    public PageSizeMap? PageSize { get; init; }

    public PageMarginMap? PageMargin { get; init; }

    public IReadOnlyList<HeaderFooterReferenceMap> Headers { get; init; } = [];

    public IReadOnlyList<HeaderFooterReferenceMap> Footers { get; init; } = [];
}

public sealed class PageSizeMap
{
    public int? WidthTwips { get; init; }

    public int? HeightTwips { get; init; }

    public string? Orientation { get; init; }
}

public sealed class PageMarginMap
{
    public int? TopTwips { get; init; }

    public int? RightTwips { get; init; }

    public int? BottomTwips { get; init; }

    public int? LeftTwips { get; init; }

    public int? HeaderTwips { get; init; }

    public int? FooterTwips { get; init; }

    public int? GutterTwips { get; init; }
}

public sealed class HeaderFooterReferenceMap
{
    public string? Type { get; init; }

    public string? RelationshipId { get; init; }
}

public sealed class TableMap
{
    public int Index { get; init; }

    public int RowCount { get; init; }

    public IReadOnlyList<int> CellCounts { get; init; } = [];

    public string TextPreview { get; init; } = "";
}
