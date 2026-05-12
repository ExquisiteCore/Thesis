namespace Thesis.Schema;

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

    public List<ProfileRoleAlias> RoleAliases { get; set; } = [];

    public List<ProfileRolePolicy> RolePolicies { get; set; } = [];

    public List<ProfileFormatCluster> FormatClusters { get; set; } = [];

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

public sealed class ProfileRoleAlias
{
    public string Alias { get; set; } = "";

    public string Role { get; set; } = "";
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

public sealed class ProfileFormatCluster
{
    public string Id { get; set; } = "";

    public string AppliesTo { get; set; } = "paragraph";

    public string RoleHint { get; set; } = "unknown";

    public int Count { get; set; }

    public double Confidence { get; set; }

    public List<string> StyleIds { get; set; } = [];

    public ProfileRoleMatch Match { get; set; } = new();

    public ParagraphFormatSample? Format { get; set; }

    public List<ProfileParagraphEvidence> Evidence { get; set; } = [];
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
