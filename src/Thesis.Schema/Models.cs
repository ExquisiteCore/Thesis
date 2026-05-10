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
