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

public sealed class OperationCatalogItem
{
    public string Op { get; set; } = "";

    public string Description { get; set; } = "";

    public List<string> TargetTypes { get; set; } = [];

    public List<string> RequiredFields { get; set; } = [];

    public List<string> OptionalFields { get; set; } = [];

    public List<string> RequiredFormat { get; set; } = [];

    public List<string> OptionalFormat { get; set; } = [];

    public bool ProfileRequired { get; set; }
}

public sealed class MatchInfo
{
    public string? Id { get; set; }

    public string? Type { get; set; }

    public string? Preview { get; set; }

    public string? PreviewBefore { get; set; }

    public string? PreviewAfter { get; set; }
}

