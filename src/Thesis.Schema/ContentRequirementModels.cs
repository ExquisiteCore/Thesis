namespace Thesis.Schema;

/// <summary>Explicit, evidence-backed requirements; no inferred requirement silently becomes a gate.</summary>
public sealed class ContentRequirement
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? StartAnchor { get; set; }
    public string? EndAnchor { get; set; }
    public string? Text { get; set; }
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public string Evidence { get; set; } = "";
}

public sealed class RequirementResult
{
    public string Id { get; set; } = "";
    public bool Passed { get; set; }
    public int? Actual { get; set; }
    public string Message { get; set; } = "";
    public string Evidence { get; set; } = "";
}
