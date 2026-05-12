namespace Thesis.Schema;

public sealed class ProfileExplanation
{
    public string ProfilePath { get; set; } = "";

    public string SourceType { get; set; } = "";

    public string SourceDocument { get; set; } = "";

    public bool RequiresFinalization { get; set; }

    public ProfileSourceEvidence SourceEvidence { get; set; } = new();

    public List<ProfileRoleExplanation> RoleSummaries { get; set; } = [];

    public ProfileTableExplanation TableSummary { get; set; } = new();

    public List<ProfileRisk> Risks { get; set; } = [];
}

public sealed class ProfileRoleExplanation
{
    public string Role { get; set; } = "";

    public string? StyleId { get; set; }

    public double Confidence { get; set; }

    public int EvidenceCount { get; set; }

    public bool HasFormat { get; set; }

    public string? SampleText { get; set; }
}

public sealed class ProfileTableExplanation
{
    public bool Detected { get; set; }

    public int TableCount { get; set; }

    public List<int> ObservedColumnCounts { get; set; } = [];

    public bool HasDefaultFormat { get; set; }

    public int ArchetypeCount { get; set; }
}

public sealed class ProfileRisk
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = "info";

    public string Message { get; set; } = "";
}

public sealed class ProfileDiff
{
    public string LeftProfilePath { get; set; } = "";

    public string RightProfilePath { get; set; } = "";

    public bool HasChanges { get; set; }

    public List<ProfileDiffChange> Changes { get; set; } = [];
}

public sealed class ProfileDiffChange
{
    public string Kind { get; set; } = "";

    public string Path { get; set; } = "";

    public string? Left { get; set; }

    public string? Right { get; set; }

    public string Message { get; set; } = "";
}

