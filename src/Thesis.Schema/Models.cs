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

    public NumberingReference? Numbering { get; set; }

    public List<DocumentRun> Runs { get; set; } = [];
}

public sealed class DocumentRun
{
    public int Index { get; set; }

    public string Text { get; set; } = "";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string? FontSizeHalfPoints { get; set; }
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
}

public sealed class DocumentNumbering
{
    public string? NumberingId { get; set; }

    public string? AbstractNumberingId { get; set; }
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
}
