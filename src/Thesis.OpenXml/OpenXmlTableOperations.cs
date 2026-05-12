using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult ApplyProfileTable(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        var profileFormat = ResolveProfileTableFormat(context.Profile, operation.Format, tableTargets, out var profileFormatError);
        if (profileFormatError is not null)
        {
            return OperationError(operation, profileFormatError);
        }

        if (profileFormat is null)
        {
            return OperationError(operation, "profile_table_format_missing");
        }

        if (!OpenXmlOperationFormatBuilder.TryCreateEffectiveTableFormat(context.ParagraphStyleIds, profileFormat, operation.Format, out var format, out var formatError))
        {
            return OperationError(operation, formatError);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var before = OpenXmlFormatReader.TableFormatPreview(target.Table);
            if (writeChanges)
            {
                OpenXmlFormatApplier.ApplyTableFormat(target.Table, format);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.TableFormatPreview(target.Table)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeTableFormat(OpenXmlFormatReader.ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static TableFormatSample? ResolveProfileTableFormat(
        TemplateProfile? profile,
        JsonNode? operationFormat,
        IReadOnlyList<ResolvedTableTarget> targets,
        out string? error)
    {
        error = null;
        var archetypeName = OpenXmlOperationJson.GetString(operationFormat, "archetype", out var archetypeError);
        if (archetypeError is not null)
        {
            error = archetypeError;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(archetypeName))
        {
            var archetypeFormat = profile?.TableArchetypes
                .Where(archetype => string.Equals(archetype.Name, archetypeName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(archetype => archetype.Confidence)
                .Select(archetype => archetype.Format)
                .FirstOrDefault(candidate => candidate is not null);
            if (archetypeFormat is null)
            {
                error = "profile_table_archetype_not_found";
                return null;
            }

            return archetypeFormat;
        }

        return profile?.TablePolicy?.Default?.Format
            ?? SelectMatchingTableArchetypeFormat(profile, targets);
    }

    private static TableFormatSample? SelectMatchingTableArchetypeFormat(
        TemplateProfile? profile,
        IReadOnlyList<ResolvedTableTarget> targets)
    {
        if (profile is null || targets.Count == 0)
        {
            return null;
        }

        return profile.TableArchetypes
            .Where(archetype => archetype.Format is not null)
            .Where(archetype => targets.All(target => TableArchetypeMatches(target, archetype.Match)))
            .OrderByDescending(archetype => archetype.Confidence)
            .Select(archetype => archetype.Format)
            .FirstOrDefault();
    }

    private static bool TableArchetypeMatches(ResolvedTableTarget target, ProfileTableMatch match)
    {
        if (match.MinRows is not null && target.RowCount < match.MinRows.Value)
        {
            return false;
        }

        if (match.MaxRows is not null && target.RowCount > match.MaxRows.Value)
        {
            return false;
        }

        return match.ColumnCounts.Count == 0
            || target.CellCounts.Any(count => match.ColumnCounts.Contains(count));
    }

    private static OperationResult SetTableBorders(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Format is not JsonObject)
        {
            return OperationError(operation, "target_value_invalid");
        }

        var format = new TableFormatSample();
        if (!OpenXmlOperationFormatBuilder.ApplyTableBordersOverride(operation.Format, format, out var formatError)
            || format.Borders is null
            || !OpenXmlOperationFormatBuilder.IsValidTableBorders(format.Borders))
        {
            return OperationError(operation, formatError.Length == 0 ? "format_value_invalid" : formatError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>())
        {
            var before = OpenXmlFormatReader.TableFormatPreview(target.Table);
            if (writeChanges)
            {
                OpenXmlFormatApplier.EnsureTableGrid(target.Table);
                OpenXmlFormatApplier.ApplyTableBorders(OpenXmlFormatApplier.GetOrCreateTableProperties(target.Table), format.Borders);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.TableFormatPreview(target.Table)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeTableFormat(OpenXmlFormatReader.ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableCellText(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
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
                OpenXmlFormatApplier.ReplaceTableCellText(target.Cell, operation.Text);
            }

            result.Matches.Add(target.ToMatchInfo(before, operation.Text));
        }

        return result;
    }

    private static OperationResult SetTableCellFormat(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!OpenXmlOperationFormatBuilder.TryCreateEffectiveFormat(context.ParagraphStyleIds, new ParagraphFormatSample(), operation.Format, out var format, out var formatError))
        {
            return OperationError(operation, formatError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.TableCell, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableCellTarget>())
        {
            var before = OpenXmlFormatReader.CellFormatPreview(target.Cell);
            if (writeChanges)
            {
                OpenXmlFormatApplier.ApplyTableCellFormat(target.Cell, format);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.CellFormatPreview(target.Cell)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeParagraphFormat(OpenXmlFormatReader.ReadFirstCellParagraphFormat(target.Cell), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableColumnWidth(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var columnIndex = OpenXmlOperationJson.GetInt(operation.Format, "columnIndex", out var columnIndexError);
        var widthTwips = OpenXmlOperationJson.GetInt(operation.Format, "widthTwips", out var widthError);
        if (columnIndexError is not null || widthError is not null || columnIndex is null || widthTwips is null
            || columnIndex < 0 || !OpenXmlOperationFormatBuilder.IsValidTwips(widthTwips))
        {
            return OperationError(operation, columnIndexError ?? widthError ?? "target_value_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        if (targets.Cast<ResolvedTableTarget>().Any(target => columnIndex.Value >= target.CellCounts.DefaultIfEmpty(0).Max()))
        {
            return OperationError(operation, "target_not_found");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>())
        {
            var before = OpenXmlFormatReader.TableFormatPreview(target.Table);
            if (writeChanges)
            {
                OpenXmlFormatApplier.ApplyTableColumnWidth(target.Table, columnIndex.Value, widthTwips.Value);
            }

            var delta = new TableFormatSample
            {
                GridColumnWidthsTwips = OpenXmlFormatApplier.GetMergedGridWidths(target.Table, columnIndex.Value, widthTwips.Value)
            };
            var after = writeChanges
                ? OpenXmlFormatReader.TableFormatPreview(target.Table)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeTableFormat(OpenXmlFormatReader.ReadTableFormat(target.Table), delta));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableRowHeader(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rowIndex = OpenXmlOperationJson.GetInt(operation.Format, "rowIndex", out var rowIndexError);
        var header = OpenXmlOperationJson.GetBool(operation.Format, "header", out var headerError);
        if (rowIndexError is not null || headerError is not null || rowIndex is null || rowIndex < 0)
        {
            return OperationError(operation, rowIndexError ?? headerError ?? "target_value_invalid");
        }

        header ??= true;
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        if (targets.Cast<ResolvedTableTarget>().Any(target => rowIndex >= target.RowCount))
        {
            return OperationError(operation, "target_not_found");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>())
        {
            var before = OpenXmlFormatReader.TableFormatPreview(target.Table);
            if (writeChanges)
            {
                OpenXmlFormatApplier.SetTableRowHeader(target.Table, rowIndex.Value, header.Value);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.TableFormatPreview(target.Table)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatReader.ReadTableFormat(target.Table));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult InsertTableRow(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rowIndex = OpenXmlOperationJson.GetInt(operation.Format, "rowIndex", out var rowIndexError);
        if (rowIndexError is not null || rowIndex is null || rowIndex < 0)
        {
            return OperationError(operation, rowIndexError ?? "target_value_invalid");
        }

        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        var cells = OpenXmlOperationJson.GetStringArray(operation.Format, "cells", out var cellsError);
        if (cellsError is not null)
        {
            return OperationError(operation, cellsError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        if (tableTargets.Any(target => rowIndex.Value >= target.RowCount))
        {
            return OperationError(operation, "target_not_found");
        }

        if (tableTargets.Any(target => cells.Count > target.CellCounts.DefaultIfEmpty(0).Max()))
        {
            return OperationError(operation, "table_cell_count_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var before = target.Table.InnerText;
            if (writeChanges)
            {
                InsertTableRow(target.Table, rowIndex.Value, position, cells);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, string.Join(" ", cells)));
        }

        return result;
    }

    private static OperationResult DeleteTableRow(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rowIndex = OpenXmlOperationJson.GetInt(operation.Format, "rowIndex", out var rowIndexError);
        if (rowIndexError is not null || rowIndex is null || rowIndex < 0)
        {
            return OperationError(operation, rowIndexError ?? "target_value_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        if (tableTargets.Any(target => rowIndex.Value >= target.RowCount))
        {
            return OperationError(operation, "target_not_found");
        }

        if (tableTargets.Any(target => target.RowCount <= 1))
        {
            return OperationError(operation, "table_row_count_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var rows = target.Table.Elements<TableRow>().ToList();
            var before = rows[rowIndex.Value].InnerText;
            if (writeChanges)
            {
                rows[rowIndex.Value].Remove();
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, ""));
        }

        return result;
    }

    private static OperationResult InsertTableColumn(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var columnIndex = OpenXmlOperationJson.GetInt(operation.Format, "columnIndex", out var columnIndexError);
        if (columnIndexError is not null || columnIndex is null || columnIndex < 0)
        {
            return OperationError(operation, columnIndexError ?? "target_value_invalid");
        }

        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        var widthTwips = OpenXmlOperationJson.GetInt(operation.Format, "widthTwips", out var widthError);
        if (widthError is not null || (widthTwips is not null && !OpenXmlOperationFormatBuilder.IsValidTwips(widthTwips)))
        {
            return OperationError(operation, widthError ?? "format_value_invalid");
        }

        var cells = OpenXmlOperationJson.GetStringArray(operation.Format, "cells", out var cellsError);
        if (cellsError is not null)
        {
            return OperationError(operation, cellsError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        if (tableTargets.Any(target => columnIndex.Value >= target.CellCounts.DefaultIfEmpty(0).Max()))
        {
            return OperationError(operation, "target_not_found");
        }

        if (tableTargets.Any(target => cells.Count > target.RowCount))
        {
            return OperationError(operation, "table_cell_count_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var before = target.Table.InnerText;
            if (writeChanges)
            {
                InsertTableColumn(target.Table, columnIndex.Value, position, cells, widthTwips);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, string.Join(" ", cells)));
        }

        return result;
    }

    private static OperationResult DeleteTableColumn(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var columnIndex = OpenXmlOperationJson.GetInt(operation.Format, "columnIndex", out var columnIndexError);
        if (columnIndexError is not null || columnIndex is null || columnIndex < 0)
        {
            return OperationError(operation, columnIndexError ?? "target_value_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var tableTargets = targets.Cast<ResolvedTableTarget>().ToList();
        if (tableTargets.Any(target => columnIndex.Value >= target.CellCounts.DefaultIfEmpty(0).Max()))
        {
            return OperationError(operation, "target_not_found");
        }

        if (tableTargets.Any(target => target.CellCounts.DefaultIfEmpty(0).Max() <= 1))
        {
            return OperationError(operation, "table_cell_count_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in tableTargets)
        {
            var before = target.Table.InnerText;
            if (writeChanges)
            {
                DeleteTableColumn(target.Table, columnIndex.Value);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, ""));
        }

        return result;
    }

    private static OperationResult ApplyThreeLineTable(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var format = new TableFormatSample
        {
            Borders = CreateThreeLineTableBorders()
        };

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>())
        {
            var before = OpenXmlFormatReader.TableFormatPreview(target.Table);
            if (writeChanges)
            {
                OpenXmlFormatApplier.EnsureTableGrid(target.Table);
                OpenXmlFormatApplier.ApplyTableBorders(OpenXmlFormatApplier.GetOrCreateTableProperties(target.Table), format.Borders);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.TableFormatPreview(target.Table)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeTableFormat(OpenXmlFormatReader.ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static TableBordersSample CreateThreeLineTableBorders()
    {
        return new TableBordersSample
        {
            Top = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
            Left = new TableBorderLineSample { Value = "nil" },
            Bottom = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
            Right = new TableBorderLineSample { Value = "nil" },
            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4", Color = "000000" },
            InsideVertical = new TableBorderLineSample { Value = "nil" }
        };
    }

    private static void InsertTableRow(Table table, int rowIndex, string position, List<string> cells)
    {
        var rows = table.Elements<TableRow>().ToList();
        var anchor = rows[rowIndex];
        var template = anchor.CloneNode(deep: true) as TableRow
            ?? throw new InvalidDataException("Could not clone table row.");
        var templateCells = template.Elements<TableCell>().ToList();
        var cellCount = templateCells.Count;
        while (templateCells.Count < cellCount)
        {
            var cell = new TableCell(new Paragraph(new Run(new Text(""))));
            template.AppendChild(cell);
            templateCells.Add(cell);
        }

        for (var index = 0; index < cellCount; index++)
        {
            OpenXmlFormatApplier.ReplaceTableCellText(templateCells[index], index < cells.Count ? cells[index] : "");
        }

        if (position == "before")
        {
            anchor.InsertBeforeSelf(template);
        }
        else
        {
            anchor.InsertAfterSelf(template);
        }
    }

    private static void InsertTableColumn(Table table, int columnIndex, string position, List<string> cells, int? widthTwips)
    {
        var insertIndex = position == "before" ? columnIndex : columnIndex + 1;
        var rows = table.Elements<TableRow>().ToList();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowCells = rows[rowIndex].Elements<TableCell>().ToList();
            var templateIndex = Math.Min(columnIndex, rowCells.Count - 1);
            var template = rowCells[templateIndex].CloneNode(deep: true) as TableCell
                ?? throw new InvalidDataException("Could not clone table cell.");
            OpenXmlFormatApplier.ReplaceTableCellText(template, rowIndex < cells.Count ? cells[rowIndex] : "");
            if (widthTwips is not null)
            {
                SetTableCellWidth(template, widthTwips.Value);
            }

            if (insertIndex >= rowCells.Count)
            {
                rows[rowIndex].AppendChild(template);
            }
            else
            {
                rowCells[insertIndex].InsertBeforeSelf(template);
            }
        }

        InsertGridColumn(table, insertIndex, widthTwips);
    }

    private static void DeleteTableColumn(Table table, int columnIndex)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (columnIndex < cells.Count)
            {
                cells[columnIndex].Remove();
            }
        }

        var gridColumns = table.TableGrid?.Elements<GridColumn>().ToList() ?? [];
        if (columnIndex < gridColumns.Count)
        {
            gridColumns[columnIndex].Remove();
        }

        OpenXmlFormatApplier.ApplyTableGrid(table, ReadTableGridWidths(table));
    }

    private static void InsertGridColumn(Table table, int columnIndex, int? widthTwips)
    {
        var widths = ReadTableGridWidths(table);
        var columnCount = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Count())
            .DefaultIfEmpty(0)
            .Max();
        while (widths.Count < columnCount - 1)
        {
            widths.Add(0);
        }

        widths.Insert(Math.Min(columnIndex, widths.Count), widthTwips ?? 0);
        OpenXmlFormatApplier.ApplyTableGrid(table, widths);
    }

    private static List<int> ReadTableGridWidths(Table table)
    {
        return table.TableGrid?
            .Elements<GridColumn>()
            .Select(column => ToInt(column.Width) ?? 0)
            .ToList() ?? [];
    }

    private static void SetTableCellWidth(TableCell cell, int widthTwips)
    {
        var properties = cell.TableCellProperties;
        if (properties is null)
        {
            properties = new TableCellProperties();
            cell.PrependChild(properties);
        }

        properties.TableCellWidth ??= new TableCellWidth();
        properties.TableCellWidth.Width = widthTwips.ToString();
        properties.TableCellWidth.Type = TableWidthUnitValues.Dxa;
    }
}
