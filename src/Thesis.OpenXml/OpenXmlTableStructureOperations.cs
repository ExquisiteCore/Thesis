using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult InsertTable(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rows = ReadRows(operation.Format?["rows"], out var rowsError);
        if (rowsError is not null || rows.Count == 0 || rows.Any(row => row.Count == 0))
        {
            return OperationError(operation, rowsError ?? "target_value_invalid");
        }

        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                var table = CreateTable(rows);
                if (position == "before")
                {
                    target.Paragraph.InsertBeforeSelf(table);
                }
                else
                {
                    target.Paragraph.InsertAfterSelf(table);
                }

                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, string.Join(" ", rows.SelectMany(row => row))));
        }

        return result;
    }

    private static OperationResult DeleteTable(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>().OrderByDescending(target => target.TableIndex))
        {
            var before = target.Table.InnerText;
            if (writeChanges)
            {
                target.Table.Remove();
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, ""));
        }

        return result;
    }

    private static OperationResult MergeCells(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rowIndex = OpenXmlOperationJson.GetInt(operation.Format, "rowIndex", out var rowError);
        var startCellIndex = OpenXmlOperationJson.GetInt(operation.Format, "startCellIndex", out var startError);
        var endCellIndex = OpenXmlOperationJson.GetInt(operation.Format, "endCellIndex", out var endError);
        if (rowError is not null || startError is not null || endError is not null
            || rowIndex is null || startCellIndex is null || endCellIndex is null
            || rowIndex < 0 || startCellIndex < 0 || endCellIndex <= startCellIndex)
        {
            return OperationError(operation, rowError ?? startError ?? endError ?? "target_value_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        if (tableTargets.Any(target => rowIndex.Value >= target.RowCount
            || endCellIndex.Value >= target.CellCounts[rowIndex.Value]))
        {
            return OperationError(operation, "target_not_found");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var before = target.Table.InnerText;
            if (writeChanges)
            {
                MergeTableCells(target.Table, rowIndex.Value, startCellIndex.Value, endCellIndex.Value);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, $"merged:{rowIndex}:{startCellIndex}-{endCellIndex}"));
        }

        return result;
    }

    private static OperationResult SplitCell(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var cellCount = OpenXmlOperationJson.GetInt(operation.Format, "cellCount", out var countError);
        var texts = OpenXmlOperationJson.GetStringArray(operation.Format, "texts", out var textsError);
        if (countError is not null || textsError is not null || cellCount is null || cellCount < 2)
        {
            return OperationError(operation, countError ?? textsError ?? "target_value_invalid");
        }

        if (texts.Count > cellCount)
        {
            return OperationError(operation, "table_cell_count_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.TableCell, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableCellTarget>())
        {
            var before = target.Cell.InnerText;
            if (writeChanges)
            {
                SplitTableCell(target.Cell, cellCount.Value, texts);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, string.Join(" ", texts)));
        }

        return result;
    }

    private static Table CreateTable(List<List<string>> rows)
    {
        var table = new Table();
        var columnCount = rows.Max(row => row.Count);
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto }));
        table.AppendChild(new TableGrid(Enumerable.Range(0, columnCount).Select(_ => new GridColumn()).Cast<OpenXmlElement>()));

        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            for (var index = 0; index < columnCount; index++)
            {
                tableRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(index < row.Count ? row[index] : "")))));
            }

            table.AppendChild(tableRow);
        }

        return table;
    }

    private static void MergeTableCells(Table table, int rowIndex, int startCellIndex, int endCellIndex)
    {
        var row = table.Elements<TableRow>().ElementAt(rowIndex);
        var cells = row.Elements<TableCell>().ToList();
        var first = cells[startCellIndex];
        var mergedText = string.Join(" ", cells.Skip(startCellIndex).Take(endCellIndex - startCellIndex + 1).Select(cell => cell.InnerText));
        OpenXmlFormatApplier.ReplaceTableCellText(first, mergedText);

        first.TableCellProperties ??= new TableCellProperties();
        first.TableCellProperties.GridSpan ??= new GridSpan();
        first.TableCellProperties.GridSpan.Val = endCellIndex - startCellIndex + 1;

        for (var index = endCellIndex; index > startCellIndex; index--)
        {
            cells[index].Remove();
        }
    }

    private static void SplitTableCell(TableCell cell, int cellCount, List<string> texts)
    {
        var row = cell.Ancestors<TableRow>().FirstOrDefault()
            ?? throw new InvalidDataException("Table cell does not have a parent row.");
        var template = cell.CloneNode(deep: true) as TableCell
            ?? throw new InvalidDataException("Could not clone table cell.");

        cell.TableCellProperties?.GridSpan?.Remove();
        OpenXmlFormatApplier.ReplaceTableCellText(cell, texts.Count > 0 ? texts[0] : "");

        var insertAfter = cell;
        for (var index = 1; index < cellCount; index++)
        {
            var clone = template.CloneNode(deep: true) as TableCell
                ?? throw new InvalidDataException("Could not clone table cell.");
            clone.TableCellProperties?.GridSpan?.Remove();
            OpenXmlFormatApplier.ReplaceTableCellText(clone, index < texts.Count ? texts[index] : "");
            insertAfter.InsertAfterSelf(clone);
            insertAfter = clone;
        }
    }

    private static List<List<string>> ReadRows(JsonNode? node, out string? error)
    {
        error = null;
        if (node is not JsonArray rowsArray)
        {
            error = "target_value_invalid";
            return [];
        }

        var rows = new List<List<string>>();
        foreach (var rowNode in rowsArray)
        {
            if (rowNode is not JsonArray rowArray)
            {
                error = "target_value_invalid";
                return [];
            }

            var row = new List<string>();
            foreach (var cellNode in rowArray)
            {
                try
                {
                    row.Add(cellNode?.GetValue<string>() ?? "");
                }
                catch (InvalidOperationException)
                {
                    error = "target_value_invalid";
                    return [];
                }
                catch (FormatException)
                {
                    error = "target_value_invalid";
                    return [];
                }
            }

            rows.Add(row);
        }

        return rows;
    }
}
