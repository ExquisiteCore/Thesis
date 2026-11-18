namespace Thesis.Schema;

/// <summary>A reviewed assembly plan. Ranges address direct body children, end exclusive.</summary>
public sealed class TemplateApplicationPlan
{
    public string? SourceSha256 { get; set; }
    public string? TemplateSha256 { get; set; }
    public bool RequireSourceCoverage { get; set; }
    public List<SkippedSourceRange> SkippedSourceRanges { get; set; } = [];
    public string SchemaVersion { get; set; } = "1.0";
    public Dictionary<string, TemplateRoleFormat> Roles { get; set; } = [];
    public List<TemplateApplicationBlock> Blocks { get; set; } = [];
    public Dictionary<string, string> SourceTextReplacements { get; set; } = [];
    public Dictionary<string, string> TemplateTextReplacements { get; set; } = [];
    public Dictionary<string, string> TocPages { get; set; } = [];
    public int ContentWidthTwips { get; set; } = 9072;
    public int? TemplateTableIndex { get; set; }
    public bool NormalizePageFooters { get; set; }
    public List<string> PendingReviews { get; set; } = [];
    public List<ContentRequirement> Requirements { get; set; } = [];
}

public sealed class TemplateApplicationBlock
{
    public string? StartAnchor { get; set; }
    public string? EndAnchor { get; set; }
    public string Type { get; set; } = "paragraph";
    public int Start { get; set; }
    public int End { get; set; }
    public string? Role { get; set; }
    public string? Text { get; set; }
    public string? ImagePath { get; set; }
    public long ImageWidthEmu { get; set; }
    public long ImageHeightEmu { get; set; }
    public List<List<string>> Rows { get; set; } = [];
    public List<int> ColumnWidths { get; set; } = [];
    public int SectionTemplateIndex { get; set; }
    public string? PageNumberFormat { get; set; }
    public int? PageNumberStart { get; set; }
    public bool FinalSection { get; set; }
    public bool NoPageNumber { get; set; }
    public bool ContinuePageNumbering { get; set; }
    public bool PageBreakBefore { get; set; }
    public string? SectionBreakType { get; set; }
    public int? ColumnCount { get; set; }
    public Dictionary<int, string> ParagraphTexts { get; set; } = [];
    public Dictionary<string, string> CellTexts { get; set; } = [];
}

public sealed class TemplateRoleFormat
{
    public int? TemplateParagraphIndex { get; set; }
    public bool ApplyTemplateInstructions { get; set; }
    public bool PreserveEmphasis { get; set; } = true;
    public string EastAsiaFont { get; set; } = "宋体";
    public string LatinFont { get; set; } = "Times New Roman";
    public int SizeHalfPoints { get; set; } = 24;
    public bool Bold { get; set; }
    public string? BoldPrefix { get; set; }
    public string Alignment { get; set; } = "left";
    public int Line { get; set; } = 360;
    public string LineRule { get; set; } = "auto";
    public int Before { get; set; }
    public int After { get; set; }
    public int FirstLineChars { get; set; }
    public int LeftTwips { get; set; }
    public int HangingTwips { get; set; }
    public int? OutlineLevel { get; set; }
    public bool KeepNext { get; set; }
    public bool PageBreakBefore { get; set; }
}

public sealed class TemplateApplicationReport
{
    public TemplatePreflightReport Preflight { get; set; } = new();
    public bool AssemblyPassed { get; set; }
    public bool StructurePassed { get; set; }
    public bool LayoutExecuted { get; set; }
    public bool LayoutPassed { get; set; }
    public bool PlanReviewPassed => Preflight.FingerprintsPinned && Preflight.UnmappedSourceBlocks.Count == 0;
    public bool ContentPassed { get; set; } = true;
    public bool AutomatedChecksPassed => AssemblyPassed && StructurePassed && LayoutExecuted && LayoutPassed && PlanReviewPassed && ContentPassed;
    public bool Ready => AutomatedChecksPassed && PendingReviews.Count == 0;
    public List<string> PendingReviews { get; set; } = [];
    public List<RequirementResult> RequirementResults { get; set; } = [];
    public string? LayoutEngine { get; set; }
    public HostApplicationReport? HostApplication { get; set; }
    public DeliveryReview? Review { get; set; }
    public List<string> StructuralErrors { get; set; } = [];
    public List<string> LayoutErrors { get; set; } = [];
    public List<string> ManualReviewItems { get; set; } = [];
    public int? RenderedPages { get; set; }
    public int RenderPasses { get; set; }
    public Dictionary<string, string> RenderedTocPages { get; set; } = [];
    public string Source { get; set; } = "";
    public string Template { get; set; } = "";
    public string Output { get; set; } = "";
    public List<int> CopiedSourceBlocks { get; set; } = [];
    public int Tables { get; set; }
    public int Drawings { get; set; }
    public int Sections { get; set; }
    public List<string> Headings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class SkippedSourceRange
{
    public int Start { get; set; }
    public int End { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class TemplatePreflightReport
{
    public string SourceSha256 { get; set; } = "";
    public string TemplateSha256 { get; set; } = "";
    public bool FingerprintsPinned { get; set; }
    public List<int> UnmappedSourceBlocks { get; set; } = [];
    public List<int> UnsupportedSourceBlocks { get; set; } = [];
    public List<SourceBlockDescriptor> SourceBlocks { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SourceBlockDescriptor
{
    public List<string> UnsupportedFeatures { get; set; } = [];
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
