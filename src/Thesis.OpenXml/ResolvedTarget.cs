using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal enum ResolvedTargetKind
{
    Paragraph,
    Run,
    Table,
    TableCell
}

internal abstract record ResolvedTarget(ResolvedTargetKind Kind)
{
    private const int PreviewLimit = 200;

    public abstract MatchInfo ToMatchInfo(string? previewBefore = null, string? previewAfter = null);

    protected static string Preview(string text)
    {
        return text.Length <= PreviewLimit ? text : text[..PreviewLimit];
    }

    protected static string? OptionalPreview(string? text)
    {
        return text is null ? null : Preview(text);
    }
}

internal sealed record ResolvedParagraphTarget(Paragraph Paragraph, int ParagraphIndex)
    : ResolvedTarget(ResolvedTargetKind.Paragraph)
{
    public override MatchInfo ToMatchInfo(string? previewBefore = null, string? previewAfter = null)
    {
        return new MatchInfo
        {
            Id = $"p{ParagraphIndex}",
            Type = "paragraph",
            Preview = Preview(Paragraph.InnerText),
            PreviewBefore = OptionalPreview(previewBefore),
            PreviewAfter = OptionalPreview(previewAfter)
        };
    }
}

internal sealed record ResolvedRunTarget(Run Run, int ParagraphIndex, int RunIndex)
    : ResolvedTarget(ResolvedTargetKind.Run)
{
    public override MatchInfo ToMatchInfo(string? previewBefore = null, string? previewAfter = null)
    {
        return new MatchInfo
        {
            Id = $"p{ParagraphIndex}:r{RunIndex}",
            Type = "run",
            Preview = Preview(Run.InnerText),
            PreviewBefore = OptionalPreview(previewBefore),
            PreviewAfter = OptionalPreview(previewAfter)
        };
    }
}

internal sealed record ResolvedTableTarget(Table Table, int TableIndex, int RowCount, List<int> CellCounts)
    : ResolvedTarget(ResolvedTargetKind.Table)
{
    public override MatchInfo ToMatchInfo(string? previewBefore = null, string? previewAfter = null)
    {
        return new MatchInfo
        {
            Id = $"t{TableIndex}",
            Type = "table",
            Preview = Preview(Table.InnerText),
            PreviewBefore = OptionalPreview(previewBefore),
            PreviewAfter = OptionalPreview(previewAfter)
        };
    }
}

internal sealed record ResolvedTableCellTarget(TableCell Cell, int TableIndex, int RowIndex, int CellIndex)
    : ResolvedTarget(ResolvedTargetKind.TableCell)
{
    public override MatchInfo ToMatchInfo(string? previewBefore = null, string? previewAfter = null)
    {
        return new MatchInfo
        {
            Id = $"t{TableIndex}:r{RowIndex}:c{CellIndex}",
            Type = "tableCell",
            Preview = Preview(Cell.InnerText),
            PreviewBefore = OptionalPreview(previewBefore),
            PreviewAfter = OptionalPreview(previewAfter)
        };
    }
}

internal sealed class TargetResolutionResult
{
    public List<ResolvedTarget> Matches { get; init; } = [];

    public string? ErrorCode { get; init; }

    public bool Success { get; init; }

    public static TargetResolutionResult FromMatches(IEnumerable<ResolvedTarget> matches)
    {
        return new TargetResolutionResult
        {
            Success = true,
            Matches = [.. matches]
        };
    }

    public static TargetResolutionResult Error(string code)
    {
        return new TargetResolutionResult
        {
            Success = false,
            ErrorCode = code
        };
    }
}
