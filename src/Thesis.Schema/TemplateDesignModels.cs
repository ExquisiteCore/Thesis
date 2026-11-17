namespace Thesis.Schema;

public sealed class TemplateDesign
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "通用论文模板";
    public string SourcePath { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int Version { get; set; } = 1;
    public double MarginTopMm { get; set; } = 25.4;
    public double MarginBottomMm { get; set; } = 25.4;
    public double MarginLeftMm { get; set; } = 30;
    public double MarginRightMm { get; set; } = 25;
    public bool ApplyMarginsToAllSections { get; set; }
    public List<TemplateDesignRole> Roles { get; set; } = [];
    public List<ContentRequirement> Requirements { get; set; } = [];
    public List<string> ReviewNotes { get; set; } = [];
}

public sealed class TemplateDesignRole
{
    public string Role { get; set; } = "body";
    public string Name { get; set; } = "正文";
    public int? SampleIndex { get; set; }
    public string SampleText { get; set; } = "";
    public double Confidence { get; set; }
    public string Font { get; set; } = "宋体";
    public string LatinFont { get; set; } = "Times New Roman";
    public double Size { get; set; } = 12;
    public bool Bold { get; set; }
    public string Alignment { get; set; } = "left";
    public double LineSpacing { get; set; } = 1.5;
    public double FirstLineCharacters { get; set; } = 2;
    public bool OverrideFormat { get; set; }
    public bool Confirmed { get; set; }
}

public sealed record ProjectIssue(string Severity, string Title, string Message, string? NodeId = null);
public sealed record ProjectHistoryEntry(string Path, DateTimeOffset CreatedAt, long Revision, string Title);
public sealed record RecentProject(string Path, string Title, DateTimeOffset LastOpened);
