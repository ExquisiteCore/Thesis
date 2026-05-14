namespace Thesis.Schema;

public sealed class ContentExtractReport
{
    public string SchemaVersion { get; set; } = "1.0";

    public string ExtractKind { get; set; } = "contentExtract";

    public string Document { get; set; } = "";

    public string? ProfilePath { get; set; }

    public string? ProjectRulesPath { get; set; }

    public string? OutputPath { get; set; }

    public bool Ready { get; set; }

    public ContentExtractSummary Summary { get; set; } = new();

    public List<ContentExtractFinding> Findings { get; set; } = [];
}

public sealed class ContentExtractSummary
{
    public string Title { get; set; } = "";

    public int ChapterCount { get; set; }

    public int TableCount { get; set; }

    public int ReferenceCount { get; set; }

    public int ParagraphCount { get; set; }

    public int HeadingCount { get; set; }

    public int SectionCount { get; set; }

    public int RequirementHintCount { get; set; }
}

public sealed class ContentExtractFinding
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = "info";

    public string Message { get; set; } = "";

    public string? Path { get; set; }
}
