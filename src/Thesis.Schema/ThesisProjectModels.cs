namespace Thesis.Schema;

/// <summary>The editable source of truth, shared by desktop and command line.</summary>
public sealed class ThesisProject
{
    public string SchemaVersion { get; set; } = "1.0";
    public string DocumentKind { get; set; } = "thesisProject";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Revision { get; set; }
    public ThesisContent Content { get; set; } = new();
    public string? TemplatePath { get; set; }
    public string? PlanPath { get; set; }
    public string? RulesPath { get; set; }
    public string? RenderConfigPath { get; set; }
    public string? TemplateDesignPath { get; set; }
    public Dictionary<string, string> CoverFields { get; set; } = [];
}
