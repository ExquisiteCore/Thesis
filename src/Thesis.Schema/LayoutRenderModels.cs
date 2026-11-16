namespace Thesis.Schema;

public sealed class LayoutRenderConfig
{
    public string LibreOfficePath { get; set; } = "";
    public string PythonPath { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxPasses { get; set; } = 4;
}

public sealed class LayoutFacts
{
    public int PageCount { get; set; }
    public Dictionary<string, string> TocPages { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> ReviewItems { get; set; } = [];
}
