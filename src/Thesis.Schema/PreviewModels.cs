namespace Thesis.Schema;

public sealed record PreviewRun(string Text, bool Bold = false, bool Italic = false, bool Strike = false, string? Link = null);
public sealed class PreviewBlock
{
    public int SourceIndex { get; init; }
    public string Kind { get; init; } = "paragraph";
    public string Font { get; init; } = "宋体";
    public double Size { get; init; } = 12;
    public string Alignment { get; init; } = "left";
    public List<PreviewRun> Runs { get; init; } = [];
    public List<List<string>> Rows { get; init; } = [];
    public byte[]? Image { get; init; }
    public string Text => string.Concat(Runs.Select(r => r.Text));
}
public sealed class DocumentPreview
{
    public List<PreviewBlock> Blocks { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}
