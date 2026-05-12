namespace Thesis.Schema;

public sealed class ProjectRules
{
    public string SchemaVersion { get; set; } = "1.0";

    public string RulesKind { get; set; } = "projectRules";

    public Dictionary<string, string> RoleAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ProjectPageSetupRules? PageSetup { get; set; }

    public Dictionary<string, ProjectParagraphFormatRule> RoleFormats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ProfileRolePolicy> RolePolicies { get; set; } = [];

    public TableFormatSample? TableDefault { get; set; }

    public List<ProfileTableArchetype> TableArchetypes { get; set; } = [];

    public List<ProfileDiagnostic> Diagnostics { get; set; } = [];
}

public sealed class ProjectPageSetupRules
{
    public PageSizeInfo? PageSize { get; set; }

    public PageMarginInfo? Margins { get; set; }
}

public sealed class ProjectParagraphFormatRule
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

    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public string? FontSizeHalfPoints { get; set; }

    public string? AsciiFont { get; set; }

    public string? HighAnsiFont { get; set; }

    public string? EastAsiaFont { get; set; }

    public string? ComplexScriptFont { get; set; }
}
