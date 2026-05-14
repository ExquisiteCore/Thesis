namespace Thesis.Schema;

public sealed class FinalAuditReport
{
    public bool Ready { get; set; }

    public string Readiness { get; set; } = "unknown";

    public string Summary { get; set; } = "";

    public Dictionary<string, string> Inputs { get; set; } = [];

    public Dictionary<string, string> Outputs { get; set; } = [];

    public List<FinalAuditStep> Steps { get; set; } = [];

    public List<FinalAuditFinding> Blocking { get; set; } = [];

    public List<FinalAuditFinding> AutoFixable { get; set; } = [];

    public List<FinalAuditFinding> RequiresWps { get; set; } = [];

    public List<FinalAuditFinding> RequiresHuman { get; set; } = [];

    public FinalAuditValidationSummary? ValidationSummary { get; set; }

    public FinalAuditHostSummary? HostSummary { get; set; }

    public FinalAuditRehearsalSummary? RehearsalSummary { get; set; }
}

public sealed class FinalAuditStep
{
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";

    public string Artifact { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class FinalAuditFinding
{
    public string Id { get; set; } = "";

    public string Severity { get; set; } = "info";

    public string Source { get; set; } = "";

    public string Message { get; set; } = "";

    public string? DiagnosticCode { get; set; }

    public string? TargetArtifact { get; set; }
}

public sealed class FinalAuditValidationSummary
{
    public FinalAuditValidationSnapshot? Before { get; set; }

    public FinalAuditValidationSnapshot? After { get; set; }
}

public sealed class FinalAuditValidationSnapshot
{
    public bool Compliant { get; set; }

    public int CheckedParagraphs { get; set; }

    public int CheckedTables { get; set; }

    public int DiagnosticCount { get; set; }
}

public sealed class FinalAuditHostSummary
{
    public bool Executed { get; set; }

    public bool Current { get; set; }

    public string RequestedHost { get; set; } = "";

    public string ProgId { get; set; } = "";

    public int? PageCount { get; set; }

    public int? ParagraphCount { get; set; }

    public int? TableCount { get; set; }

    public int? FieldCount { get; set; }

    public int? TableOfContentsCount { get; set; }
}

public sealed class FinalAuditRehearsalSummary
{
    public bool ReadyForFinalReview { get; set; }

    public double HeadingCoverage { get; set; }

    public int MissingReferenceParagraphCount { get; set; }

    public int MissingReferenceTableCount { get; set; }

    public int GapCount { get; set; }

    public int DiagnosticCount { get; set; }
}

public sealed class RepairPlan
{
    public bool Ready { get; set; }

    public List<RepairPlanItem> Items { get; set; } = [];
}

public sealed class RepairPlanItem
{
    public string IssueId { get; set; } = "";

    public string Severity { get; set; } = "info";

    public string Source { get; set; } = "";

    public string TargetArtifact { get; set; } = "";

    public string SuggestedCommand { get; set; } = "";

    public bool Automatic { get; set; }

    public bool RequiresWps { get; set; }

    public string Explanation { get; set; } = "";
}
