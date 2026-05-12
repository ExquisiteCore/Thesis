namespace Thesis.Schema;

public sealed class FinalizationPlan
{
    public bool Required { get; set; }

    public List<string> Reasons { get; set; } = [];

    public List<FinalizationStep> Steps { get; set; } = [];
}

public sealed class FinalizationStep
{
    public string Id { get; set; } = "";

    public string Capability { get; set; } = "";

    public string Description { get; set; } = "";

    public bool Required { get; set; }
}

public sealed class HostApplicationReport
{
    public string Action { get; set; } = "";

    public string RequestedHost { get; set; } = "";

    public string ProgId { get; set; } = "";

    public string Document { get; set; } = "";

    public bool Executed { get; set; }

    public HostLayoutMetrics Layout { get; set; } = new();

    public List<HostApplicationStep> Steps { get; set; } = [];
}

public sealed class HostLayoutMetrics
{
    public int? PageCount { get; set; }

    public int? ParagraphCount { get; set; }

    public int? TableCount { get; set; }

    public int? FieldCount { get; set; }

    public int? TableOfContentsCount { get; set; }
}

public sealed class HostApplicationStep
{
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";

    public string Message { get; set; } = "";
}

