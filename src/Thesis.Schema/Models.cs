using System.Text.Json.Nodes;

namespace Thesis.Schema;

public sealed class OperationRequest
{
    public string SchemaVersion { get; set; } = "1.0";

    public string? RequestId { get; set; }

    public RequestMode Mode { get; set; } = RequestMode.DryRun;

    public RunOptions Options { get; set; } = new();

    public JsonObject? ProfileOverrides { get; set; }

    public List<ThesisOperation> Operations { get; set; } = [];
}

public sealed class RunOptions
{
    public bool CreateSnapshot { get; set; } = true;

    public bool StopOnError { get; set; } = true;

    public bool RequireSingleMatch { get; set; }

    public bool TrackChanges { get; set; }
}

public sealed class ThesisOperation
{
    public string? Id { get; set; }

    public string? Op { get; set; }

    public string? Role { get; set; }

    public JsonNode? Target { get; set; }

    public string? Text { get; set; }

    public JsonNode? Format { get; set; }

    public JsonNode? MatchPolicy { get; set; }
}

public sealed class OperationResult
{
    public string? Id { get; set; }

    public string Status { get; set; } = "pending";

    public string? Reason { get; set; }

    public List<MatchInfo> Matches { get; set; } = [];
}

public sealed class DocumentEditResult
{
    public List<OperationResult> Operations { get; set; } = [];

    public List<Diagnostic> Diagnostics { get; set; } = [];
}

public sealed class CliResult
{
    public string SchemaVersion { get; set; } = "1.0";

    public string Status { get; set; } = "success";

    public string? RequestId { get; set; }

    public RequestMode? Mode { get; set; }

    public string? Workspace { get; set; }

    public string? Document { get; set; }

    public string? OutputPath { get; set; }

    public SessionState? Session { get; set; }

    public DocumentMap? DocumentMap { get; set; }

    public SnapshotInfo? Snapshot { get; set; }

    public List<SnapshotInfo> Snapshots { get; set; } = [];

    public List<OperationResult> Operations { get; set; } = [];

    public List<Diagnostic> Diagnostics { get; set; } = [];
}

public sealed class SessionState
{
    public string SchemaVersion { get; set; } = "1.0";

    public string OriginalPath { get; set; } = "";

    public string WorkingPath { get; set; } = "";

    public string ProfilePath { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public int SnapshotCounter { get; set; }
}

public sealed class Diagnostic
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = "info";

    public string Message { get; set; } = "";

    public string? Path { get; set; }
}

public sealed class SnapshotInfo
{
    public bool Created { get; set; }

    public string? Id { get; set; }

    public string? Path { get; set; }
}

public sealed class MatchInfo
{
    public string? Id { get; set; }

    public string? Type { get; set; }

    public string? Preview { get; set; }

    public string? PreviewBefore { get; set; }

    public string? PreviewAfter { get; set; }
}

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

public sealed class TemplateProfile
{
    public string SchemaVersion { get; set; } = "1.0";

    public string ProfileKind { get; set; } = "templateProfile";

    public string SourceType { get; set; } = "";

    public string SourceDocument { get; set; } = "";

    public bool RequiresFinalization { get; set; }

    public List<string> FinalizationReasons { get; set; } = [];

    public ProfilePageSetup PageSetup { get; set; } = new();

    public List<ProfileStyleRole> StyleRoles { get; set; } = [];

    public List<ProfileRolePolicy> RolePolicies { get; set; } = [];

    public ProfileNumberingPolicy NumberingPolicy { get; set; } = new();

    public ProfileTablePolicy TablePolicy { get; set; } = new();

    public List<ProfileTableArchetype> TableArchetypes { get; set; } = [];

    public List<ProfileDiagnostic> Diagnostics { get; set; } = [];

    public ProfileSourceEvidence SourceEvidence { get; set; } = new();
}

public sealed class ProfilePageSetup
{
    public PageSizeInfo? PageSize { get; set; }

    public PageMarginInfo? Margins { get; set; }

    public List<HeaderFooterReference> Headers { get; set; } = [];

    public List<HeaderFooterReference> Footers { get; set; } = [];
}

public sealed class ProfileStyleRole
{
    public string Role { get; set; } = "";

    public string? StyleId { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? BasedOn { get; set; }

    public double Confidence { get; set; }

    public ParagraphFormatSample? Format { get; set; }

    public List<ProfileParagraphEvidence> Evidence { get; set; } = [];
}

public sealed class ProfileRolePolicy
{
    public string Role { get; set; } = "";

    public string AppliesTo { get; set; } = "paragraph";

    public int Priority { get; set; }

    public double Confidence { get; set; }

    public ProfileRoleMatch Match { get; set; } = new();

    public ParagraphFormatSample? Format { get; set; }
}

public sealed class ProfileRoleMatch
{
    public List<string> StyleIds { get; set; } = [];

    public List<string> TextPatterns { get; set; } = [];

    public List<int> OutlineLevels { get; set; } = [];

    public ProfileRoleFormatMatch? Format { get; set; }
}

public sealed class ProfileRoleFormatMatch
{
    public string? StyleId { get; set; }

    public string? Alignment { get; set; }

    public string? FontSizeHalfPoints { get; set; }

    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public string? LineSpacing { get; set; }

    public string? LineSpacingRule { get; set; }

    public IntRangeMatch? FirstLineIndentTwips { get; set; }

    public IntRangeMatch? LeftIndentTwips { get; set; }

    public IntRangeMatch? RightIndentTwips { get; set; }
}

public sealed class IntRangeMatch
{
    public int? Min { get; set; }

    public int? Max { get; set; }

    public int? Exact { get; set; }
}

public sealed class ProfileParagraphEvidence
{
    public int ParagraphIndex { get; set; }

    public string? StyleId { get; set; }

    public string TextPreview { get; set; } = "";
}

public sealed class ProfileNumberingPolicy
{
    public bool Detected { get; set; }

    public List<DocumentNumbering> Instances { get; set; } = [];

    public List<ProfileNumberingUse> ParagraphUses { get; set; } = [];
}

public sealed class ProfileNumberingUse
{
    public int ParagraphIndex { get; set; }

    public string? NumberingId { get; set; }

    public string? Level { get; set; }

    public string TextPreview { get; set; } = "";
}

public sealed class ProfileTablePolicy
{
    public bool Detected { get; set; }

    public int TableCount { get; set; }

    public List<int> ObservedColumnCounts { get; set; } = [];

    public ProfileTableSample? Default { get; set; }
}

public sealed class ProfileTableSample
{
    public int RowCount { get; set; }

    public List<int> CellCounts { get; set; } = [];

    public string TextPreview { get; set; } = "";

    public TableFormatSample? Format { get; set; }
}

public sealed class ProfileTableArchetype
{
    public string Name { get; set; } = "";

    public double Confidence { get; set; }

    public ProfileTableMatch Match { get; set; } = new();

    public TableFormatSample? Format { get; set; }
}

public sealed class ProfileTableMatch
{
    public int? MinRows { get; set; }

    public int? MaxRows { get; set; }

    public List<int> ColumnCounts { get; set; } = [];
}

public sealed class ProfileDiagnostic
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = "info";

    public string Message { get; set; } = "";

    public List<string> Evidence { get; set; } = [];
}

public sealed class ProfileSourceEvidence
{
    public int ParagraphCount { get; set; }

    public int StyleCount { get; set; }

    public int NumberingCount { get; set; }

    public int SectionCount { get; set; }

    public int TableCount { get; set; }

    public List<ProfileParagraphEvidence> ParagraphSamples { get; set; } = [];
}
