namespace Thesis.Schema;

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

    public FinalizationPlan? FinalizationPlan { get; set; }

    public HostApplicationReport? HostApplication { get; set; }

    public ProfileExplanation? ProfileExplanation { get; set; }

    public ProfileDiff? ProfileDiff { get; set; }

    public ValidationReport? Validation { get; set; }

    public List<OperationCatalogItem> OperationsCatalog { get; set; } = [];

    public OperationRequest? OperationSample { get; set; }

    public SnapshotInfo? Snapshot { get; set; }

    public List<SnapshotInfo> Snapshots { get; set; } = [];

    public List<OperationResult> Operations { get; set; } = [];

    public List<Diagnostic> Diagnostics { get; set; } = [];
}

public sealed class Diagnostic
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = "info";

    public string Message { get; set; } = "";

    public string? Path { get; set; }
}

public sealed class ValidationReport
{
    public bool Compliant { get; set; }

    public int CheckedParagraphs { get; set; }

    public int CheckedTables { get; set; }

    public List<Diagnostic> Diagnostics { get; set; } = [];

    public List<ThesisOperation> SuggestedOperations { get; set; } = [];
}

