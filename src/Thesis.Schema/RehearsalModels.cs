namespace Thesis.Schema;

public sealed class RehearsalComparisonReport
{
    public string CandidateDocument { get; set; } = "";

    public string ReferenceDocument { get; set; } = "";

    public bool ReadyForFinalReview { get; set; }

    public RehearsalDocumentSummary Candidate { get; set; } = new();

    public RehearsalDocumentSummary Reference { get; set; } = new();

    public RehearsalContentCoverage ContentCoverage { get; set; } = new();

    public ValidationReport? Validation { get; set; }

    public List<Diagnostic> Diagnostics { get; set; } = [];
}

public sealed class RehearsalDocumentSummary
{
    public int ParagraphCount { get; set; }

    public int NonEmptyParagraphCount { get; set; }

    public int CharacterCount { get; set; }

    public int TableCount { get; set; }

    public int SectionCount { get; set; }

    public bool RequiresFinalization { get; set; }

    public bool HostFinalizationCurrent { get; set; }

    public List<string> Headings { get; set; } = [];
}

public sealed class RehearsalContentCoverage
{
    public int ReferenceHeadingCount { get; set; }

    public int MatchedHeadingCount { get; set; }

    public double HeadingCoverage { get; set; }
}
