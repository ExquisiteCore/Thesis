using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class OpenXmlMicroEditor
{
    private const int PreviewLimit = 200;
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static DocumentEditResult Apply(string docxPath, OperationRequest request)
    {
        return Apply(docxPath, request, profile: null);
    }

    public static DocumentEditResult Apply(string docxPath, OperationRequest request, TemplateProfile? profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Operations.Count == 0)
        {
            return new DocumentEditResult();
        }

        if (request.Mode is RequestMode.DryRun or RequestMode.ValidateOnly)
        {
            return Edit(docxPath, request, profile, writeChanges: false);
        }

        var fullPath = Path.GetFullPath(docxPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Document path has no parent directory.");
        var tempPath = Path.Combine(directory, Path.GetFileName(fullPath) + ".run-" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var baselineValidationErrors = GetValidationErrors(fullPath);
            File.Copy(fullPath, tempPath);
            var result = Edit(tempPath, request, profile, writeChanges: true);
            if (HasError(result))
            {
                return result;
            }

            if (!HasAppliedOperation(result))
            {
                return result;
            }

            OpenXmlDocumentInspector.Inspect(tempPath);
            var validation = ValidatePackage(tempPath, baselineValidationErrors);
            if (validation is not null)
            {
                MarkAppliedOperationsAsPreview(result);
                result.Diagnostics.Add(validation);
                return result;
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return result;
        }
        catch (Exception ex) when (IsExpectedEditFailure(ex))
        {
            return Error("document_edit_failed", $"Working document could not be edited: {ex.Message}", fullPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static DocumentEditResult Edit(string docxPath, OperationRequest request, TemplateProfile? profile, bool writeChanges)
    {
        try
        {
            using var document = WordprocessingDocument.Open(Path.GetFullPath(docxPath), isEditable: writeChanges);
            var mainPart = document.MainDocumentPart
                ?? throw new InvalidDataException("DOCX does not contain a main document part.");
            var wordDocument = mainPart.Document
                ?? throw new InvalidDataException("DOCX does not contain a document.");
            var body = wordDocument.Body
                ?? throw new InvalidDataException("DOCX does not contain a document body.");

            var context = new EditContext(
                ReadParagraphStyles(mainPart),
                new OpenXmlTargetResolver(body, profile, request.ProfileOverrides, ReadStyleOutlineLevels(mainPart)),
                profile,
                request.ProfileOverrides);
            var result = new DocumentEditResult();

            foreach (var operation in request.Operations)
            {
                var operationResult = ApplyOperation(context, request.Options, operation, writeChanges);
                result.Operations.Add(operationResult);
                if (operationResult.Status == "error")
                {
                    result.Diagnostics.Add(new Diagnostic
                    {
                        Severity = "error",
                        Code = operationResult.Reason ?? "operation_failed",
                        Message = $"Operation failed: {operation.Id ?? operation.Op ?? "unnamed"}"
                    });

                    if (request.Options.StopOnError)
                    {
                        break;
                    }
                }
            }

            if (writeChanges && HasError(result))
            {
                MarkAppliedOperationsAsPreview(result);
            }
            else if (writeChanges && HasAppliedOperation(result))
            {
                wordDocument.Save();
            }

            return result;
        }
        catch (Exception ex) when (IsExpectedEditFailure(ex))
        {
            return Error("document_edit_failed", $"Working document could not be edited: {ex.Message}", Path.GetFullPath(docxPath));
        }
    }

    private static OperationResult ApplyOperation(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return operation.Op switch
        {
            "resolveTarget" => ResolveTarget(context, options, operation),
            "replaceParagraphText" => ReplaceParagraphText(context, options, operation, writeChanges),
            "setParagraphStyle" => SetParagraphStyle(context, options, operation, writeChanges),
            "setRunFormat" => SetRunFormat(context, operation, writeChanges),
            "applyProfileRole" => ApplyProfileRole(context, options, operation, writeChanges),
            "applyProfileTable" => ApplyProfileTable(context, options, operation, writeChanges),
            "setTableBorders" => SetTableBorders(context, options, operation, writeChanges),
            "setTableCellText" => SetTableCellText(context, options, operation, writeChanges),
            "setTableCellFormat" => SetTableCellFormat(context, options, operation, writeChanges),
            "setTableColumnWidth" => SetTableColumnWidth(context, options, operation, writeChanges),
            "setTableRowHeader" => SetTableRowHeader(context, options, operation, writeChanges),
            "applyThreeLineTable" => ApplyThreeLineTable(context, options, operation, writeChanges),
            null or "" => OperationError(operation, "operation_missing"),
            _ => OperationError(operation, "operation_unknown")
        };
    }

    private static OperationResult ResolveTarget(EditContext context, RunOptions options, ThesisOperation operation)
    {
        var resolution = context.Resolver.Resolve(operation.Target, options);
        if (!resolution.Success)
        {
            return OperationError(operation, resolution.ErrorCode!);
        }

        var result = OperationSuccess(operation, "preview");
        result.Matches.AddRange(resolution.Matches.Select(match => match.ToMatchInfo()));
        return result;
    }

    private static OperationResult ReplaceParagraphText(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var paragraph = target.Paragraph;
            var before = paragraph.InnerText;
            if (writeChanges)
            {
                if (HasUnsupportedParagraphContent(paragraph))
                {
                    return OperationError(operation, "paragraph_structure_unsupported");
                }

                ReplaceParagraphRuns(paragraph, operation.Text);
            }

            result.Matches.Add(target.ToMatchInfo(before, operation.Text));
        }

        return result;
    }

    private static OperationResult SetParagraphStyle(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var styleId = GetString(operation.Format, "styleId", out var formatError);
        if (formatError is not null)
        {
            return OperationError(operation, formatError);
        }

        if (string.IsNullOrWhiteSpace(styleId))
        {
            return OperationError(operation, "style_id_missing");
        }

        if (!context.ParagraphStyleIds.Contains(styleId))
        {
            return OperationError(operation, "paragraph_style_missing");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var paragraph = target.Paragraph;
            var before = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (writeChanges)
            {
                var properties = GetOrCreateParagraphProperties(paragraph);
                var paragraphStyle = properties.ParagraphStyleId;
                if (paragraphStyle is null)
                {
                    paragraphStyle = new ParagraphStyleId();
                    properties.PrependChild(paragraphStyle);
                }

                paragraphStyle.Val = styleId;
            }

            result.Matches.Add(target.ToMatchInfo(before ?? "", styleId));
        }

        return result;
    }

    private static OperationResult SetRunFormat(EditContext context, ThesisOperation operation, bool writeChanges)
    {
        var singleRun = new RunOptions
        {
            CreateSnapshot = false,
            StopOnError = true,
            RequireSingleMatch = true,
            TrackChanges = false
        };
        if (!TryResolveTargets(context, singleRun, operation, ResolvedTargetKind.Run, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var target = (ResolvedRunTarget)targets.Single();
        var run = target.Run;
        var size = GetString(operation.Format, "fontSizeHalfPoints", out var formatError);
        if (formatError is not null)
        {
            return OperationError(operation, formatError);
        }

        if (size is not null && !IsValidHalfPointSize(size))
        {
            return OperationError(operation, "font_size_invalid");
        }

        var before = RunPreview(run);
        if (writeChanges)
        {
            var properties = GetOrCreateRunProperties(run);
            if (!ApplyBooleanRunProperty(properties, operation.Format, "bold", () => new Bold(), properties.GetFirstChild<Bold>(), out var boldError))
            {
                return OperationError(operation, boldError);
            }

            if (!ApplyBooleanRunProperty(properties, operation.Format, "italic", () => new Italic(), properties.GetFirstChild<Italic>(), out var italicError))
            {
                return OperationError(operation, italicError);
            }

            if (size is not null)
            {
                properties.FontSize ??= new FontSize();
                properties.FontSize.Val = size;
            }
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(target.ToMatchInfo(before, FormatPreview(operation.Format)));
        return result;
    }

    private static OperationResult ApplyProfileRole(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var profileFormat = ProfileRoleResolver.FindRoleFormat(
            context.Profile,
            context.ProfileOverrides,
            operation.Role,
            out var roleError);
        if (roleError is not null)
        {
            return OperationError(operation, roleError);
        }

        if (profileFormat is null)
        {
            return OperationError(operation, "profile_role_format_missing");
        }

        if (!TryCreateEffectiveFormat(context, profileFormat, operation.Format, out var format, out var formatError))
        {
            return OperationError(operation, formatError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var before = ParagraphFormatPreview(target.Paragraph);
            if (writeChanges)
            {
                ApplyParagraphFormat(target.Paragraph, format);
            }

            var after = writeChanges
                ? ParagraphFormatPreview(target.Paragraph)
                : FormatPreview(MergeParagraphFormat(ReadParagraphFormat(target.Paragraph), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult ApplyProfileTable(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var profileFormat = context.Profile?.TablePolicy?.Default?.Format;
        if (profileFormat is null)
        {
            return OperationError(operation, "profile_table_format_missing");
        }

        if (!TryCreateEffectiveTableFormat(context, profileFormat, operation.Format, out var format, out var formatError))
        {
            return OperationError(operation, formatError);
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Table, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedTableTarget>())
        {
            var before = TableFormatPreview(target.Table);
            if (writeChanges)
            {
                ApplyTableFormat(target.Table, format);
            }

            var after = writeChanges
                ? TableFormatPreview(target.Table)
                : FormatPreview(MergeTableFormat(ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableBorders(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Format is not JsonObject)
        {
            return OperationError(operation, "target_value_invalid");
        }

        var format = new TableFormatSample();
        if (!ApplyTableBordersOverride(operation.Format, format, out var formatError)
            || format.Borders is null
            || !IsValidTableBorders(format.Borders))
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
            var before = TableFormatPreview(target.Table);
            if (writeChanges)
            {
                EnsureTableGrid(target.Table);
                ApplyTableBorders(GetOrCreateTableProperties(target.Table), format.Borders);
            }

            var after = writeChanges
                ? TableFormatPreview(target.Table)
                : FormatPreview(MergeTableFormat(ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableCellText(
        EditContext context,
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
                ReplaceTableCellText(target.Cell, operation.Text);
            }

            result.Matches.Add(target.ToMatchInfo(before, operation.Text));
        }

        return result;
    }

    private static OperationResult SetTableCellFormat(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryCreateEffectiveFormat(context, new ParagraphFormatSample(), operation.Format, out var format, out var formatError))
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
            var before = CellFormatPreview(target.Cell);
            if (writeChanges)
            {
                ApplyTableCellFormat(target.Cell, format);
            }

            var after = writeChanges
                ? CellFormatPreview(target.Cell)
                : FormatPreview(MergeParagraphFormat(ReadFirstCellParagraphFormat(target.Cell), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableColumnWidth(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var columnIndex = GetInt(operation.Format, "columnIndex", out var columnIndexError);
        var widthTwips = GetInt(operation.Format, "widthTwips", out var widthError);
        if (columnIndexError is not null || widthError is not null || columnIndex is null || widthTwips is null
            || columnIndex < 0 || !IsValidTwips(widthTwips))
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
            var before = TableFormatPreview(target.Table);
            if (writeChanges)
            {
                ApplyTableColumnWidth(target.Table, columnIndex.Value, widthTwips.Value);
            }

            var delta = new TableFormatSample
            {
                GridColumnWidthsTwips = GetMergedGridWidths(target.Table, columnIndex.Value, widthTwips.Value)
            };
            var after = writeChanges
                ? TableFormatPreview(target.Table)
                : FormatPreview(MergeTableFormat(ReadTableFormat(target.Table), delta));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult SetTableRowHeader(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var rowIndex = GetInt(operation.Format, "rowIndex", out var rowIndexError);
        var header = GetBool(operation.Format, "header", out var headerError);
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
            var before = TableFormatPreview(target.Table);
            if (writeChanges)
            {
                SetTableRowHeader(target.Table, rowIndex.Value, header.Value);
            }

            var after = writeChanges
                ? TableFormatPreview(target.Table)
                : FormatPreview(ReadTableFormat(target.Table));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static OperationResult ApplyThreeLineTable(
        EditContext context,
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
            var before = TableFormatPreview(target.Table);
            if (writeChanges)
            {
                EnsureTableGrid(target.Table);
                ApplyTableBorders(GetOrCreateTableProperties(target.Table), format.Borders);
            }

            var after = writeChanges
                ? TableFormatPreview(target.Table)
                : FormatPreview(MergeTableFormat(ReadTableFormat(target.Table), format));
            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static bool TryCreateEffectiveFormat(
        EditContext context,
        ParagraphFormatSample profileFormat,
        JsonNode? overrideFormat,
        out ParagraphFormatSample format,
        out string error)
    {
        format = Clone(profileFormat);
        error = "";

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        if (!ApplyParagraphOverride(overrideFormat, format, "styleId", (target, value) => target.StyleId = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "alignment", (target, value) => target.Alignment = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "lineSpacing", (target, value) => target.LineSpacing = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "lineSpacingRule", (target, value) => target.LineSpacingRule = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "spacingBeforeTwips", (target, value) => target.SpacingBeforeTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "spacingAfterTwips", (target, value) => target.SpacingAfterTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "firstLineIndentTwips", (target, value) => target.FirstLineIndentTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "leftIndentTwips", (target, value) => target.LeftIndentTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "rightIndentTwips", (target, value) => target.RightIndentTwips = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "fontSizeHalfPoints", (target, value) => target.FontSizeHalfPoints = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "asciiFont", (target, value) => target.AsciiFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "highAnsiFont", (target, value) => target.HighAnsiFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "eastAsiaFont", (target, value) => target.EastAsiaFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "complexScriptFont", (target, value) => target.ComplexScriptFont = value, out error)
            || !ApplyRunBoolOverride(overrideFormat, format, "bold", (target, value) => target.Bold = value, out error)
            || !ApplyRunBoolOverride(overrideFormat, format, "italic", (target, value) => target.Italic = value, out error))
        {
            return false;
        }

        if (!TryValidateParagraphFormat(context, format, out error))
        {
            return false;
        }

        format.Alignment = NormalizeAlignment(format.Alignment);
        format.LineSpacingRule = NormalizeLineSpacingRule(format.LineSpacingRule);
        return true;
    }

    private static bool TryCreateEffectiveTableFormat(
        EditContext context,
        TableFormatSample profileFormat,
        JsonNode? overrideFormat,
        out TableFormatSample format,
        out string error)
    {
        format = Clone(profileFormat);
        error = "";

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        if (!ApplyTableStringOverride(overrideFormat, format, "widthType", (target, value) => target.WidthType = value, out error)
            || !ApplyTableStringOverride(overrideFormat, format, "alignment", (target, value) => target.Alignment = value, out error)
            || !ApplyTableIntOverride(overrideFormat, format, "widthTwips", (target, value) => target.WidthTwips = value, out error)
            || !ApplyTableIntOverride(overrideFormat, format, "headerRowCount", (target, value) => target.HeaderRowCount = value, out error)
            || !ApplyGridColumnWidthsOverride(overrideFormat, format, out error)
            || !ApplyTableBordersOverride(overrideFormat, format, out error)
            || !ApplyTableCellMarginsOverride(overrideFormat, format, out error))
        {
            return false;
        }

        if (!TryValidateTableFormat(context, format, out error))
        {
            return false;
        }

        format.WidthType = NormalizeTableWidthType(format.WidthType);
        format.Alignment = NormalizeAlignment(format.Alignment);
        if (format.FirstCellParagraphFormat is not null)
        {
            format.FirstCellParagraphFormat.Alignment = NormalizeAlignment(format.FirstCellParagraphFormat.Alignment);
            format.FirstCellParagraphFormat.LineSpacingRule = NormalizeLineSpacingRule(format.FirstCellParagraphFormat.LineSpacingRule);
        }

        return true;
    }

    private static bool ApplyParagraphOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<ParagraphFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyIntParagraphOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<ParagraphFormatSample, int> apply,
        out string error)
    {
        var value = GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyRunStringOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<RunFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            format.RunFormat ??= new RunFormatSample();
            apply(format.RunFormat, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyRunBoolOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<RunFormatSample, bool> apply,
        out string error)
    {
        var value = GetBool(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            format.RunFormat ??= new RunFormatSample();
            apply(format.RunFormat, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableStringOverride(
        JsonNode? overrideFormat,
        TableFormatSample format,
        string propertyName,
        Action<TableFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableIntOverride(
        JsonNode? overrideFormat,
        TableFormatSample format,
        string propertyName,
        Action<TableFormatSample, int> apply,
        out string error)
    {
        var value = GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyGridColumnWidthsOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var value = overrideFormat?["gridColumnWidthsTwips"];
        if (value is null)
        {
            return true;
        }

        if (value is not JsonArray widths)
        {
            error = "target_value_invalid";
            return false;
        }

        var parsed = new List<int>();
        foreach (var item in widths)
        {
            if (!TryGetJsonValue(item, out int width))
            {
                error = "target_value_invalid";
                return false;
            }

            parsed.Add(width);
        }

        format.GridColumnWidthsTwips = parsed;
        return true;
    }

    private static bool ApplyTableBordersOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var bordersNode = overrideFormat?["borders"];
        if (bordersNode is null)
        {
            return true;
        }

        if (bordersNode is not JsonObject borders)
        {
            error = "target_value_invalid";
            return false;
        }

        format.Borders ??= new TableBordersSample();
        return ApplyBorderOverride(borders, "top", format.Borders.Top, value => format.Borders.Top = value, out error)
            && ApplyBorderOverride(borders, "bottom", format.Borders.Bottom, value => format.Borders.Bottom = value, out error)
            && ApplyBorderOverride(borders, "left", format.Borders.Left, value => format.Borders.Left = value, out error)
            && ApplyBorderOverride(borders, "right", format.Borders.Right, value => format.Borders.Right = value, out error)
            && ApplyBorderOverride(borders, "insideHorizontal", format.Borders.InsideHorizontal, value => format.Borders.InsideHorizontal = value, out error)
            && ApplyBorderOverride(borders, "insideVertical", format.Borders.InsideVertical, value => format.Borders.InsideVertical = value, out error);
    }

    private static bool ApplyBorderOverride(
        JsonObject borders,
        string propertyName,
        TableBorderLineSample? target,
        Action<TableBorderLineSample> assign,
        out string error)
    {
        error = "";
        if (!borders.TryGetPropertyValue(propertyName, out var borderNode) || borderNode is null)
        {
            return true;
        }

        if (borderNode is not JsonObject border)
        {
            error = "target_value_invalid";
            return false;
        }

        target ??= new TableBorderLineSample();
        assign(target);
        return ApplyBorderStringOverride(border, target, "value", (line, value) => line.Value = value, out error)
            && ApplyBorderStringOverride(border, target, "size", (line, value) => line.Size = value, out error)
            && ApplyBorderStringOverride(border, target, "color", (line, value) => line.Color = value, out error)
            && ApplyBorderStringOverride(border, target, "space", (line, value) => line.Space = value, out error);
    }

    private static bool ApplyBorderStringOverride(
        JsonObject border,
        TableBorderLineSample line,
        string propertyName,
        Action<TableBorderLineSample, string> apply,
        out string error)
    {
        var value = GetString(border, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(line, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableCellMarginsOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var marginsNode = overrideFormat?["cellMargins"];
        if (marginsNode is null)
        {
            return true;
        }

        if (marginsNode is not JsonObject margins)
        {
            error = "target_value_invalid";
            return false;
        }

        format.CellMargins ??= new TableCellMarginsSample();
        return ApplyMarginOverride(margins, format.CellMargins, "topTwips", (target, value) => target.TopTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "rightTwips", (target, value) => target.RightTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "bottomTwips", (target, value) => target.BottomTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "leftTwips", (target, value) => target.LeftTwips = value, out error);
    }

    private static bool ApplyMarginOverride(
        JsonObject margins,
        TableCellMarginsSample target,
        string propertyName,
        Action<TableCellMarginsSample, int> apply,
        out string error)
    {
        var value = GetInt(margins, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(target, value.Value);
        }

        error = "";
        return true;
    }

    private static void ApplyParagraphFormat(Paragraph paragraph, ParagraphFormatSample format)
    {
        var properties = GetOrCreateParagraphProperties(paragraph);

        if (!string.IsNullOrWhiteSpace(format.StyleId))
        {
            var paragraphStyle = properties.ParagraphStyleId;
            if (paragraphStyle is null)
            {
                paragraphStyle = new ParagraphStyleId();
                properties.PrependChild(paragraphStyle);
            }

            paragraphStyle.Val = format.StyleId;
        }

        if (!string.IsNullOrWhiteSpace(format.Alignment))
        {
            properties.Justification ??= new Justification();
            SetWordprocessingAttribute(properties.Justification, "val", format.Alignment);
        }

        if (format.SpacingBeforeTwips is not null
            || format.SpacingAfterTwips is not null
            || format.LineSpacing is not null
            || format.LineSpacingRule is not null)
        {
            properties.SpacingBetweenLines ??= new SpacingBetweenLines();
            if (format.SpacingBeforeTwips is not null)
            {
                properties.SpacingBetweenLines.Before = format.SpacingBeforeTwips.Value.ToString();
            }

            if (format.SpacingAfterTwips is not null)
            {
                properties.SpacingBetweenLines.After = format.SpacingAfterTwips.Value.ToString();
            }

            if (format.LineSpacing is not null)
            {
                properties.SpacingBetweenLines.Line = format.LineSpacing;
            }

            if (format.LineSpacingRule is not null)
            {
                SetWordprocessingAttribute(properties.SpacingBetweenLines, "lineRule", format.LineSpacingRule);
            }
        }

        if (format.FirstLineIndentTwips is not null
            || format.LeftIndentTwips is not null
            || format.RightIndentTwips is not null)
        {
            properties.Indentation ??= new Indentation();
            if (format.FirstLineIndentTwips is not null)
            {
                properties.Indentation.FirstLine = format.FirstLineIndentTwips.Value.ToString();
            }

            if (format.LeftIndentTwips is not null)
            {
                properties.Indentation.Left = format.LeftIndentTwips.Value.ToString();
            }

            if (format.RightIndentTwips is not null)
            {
                properties.Indentation.Right = format.RightIndentTwips.Value.ToString();
            }
        }

        if (format.RunFormat is not null)
        {
            ApplyRunFormat(paragraph, format.RunFormat);
        }
    }

    private static void ApplyRunFormat(Paragraph paragraph, RunFormatSample format)
    {
        foreach (var run in paragraph.Descendants<Run>().Where(run => run.Descendants<Text>().Any()))
        {
            var properties = GetOrCreateRunProperties(run);

            if (format.AsciiFont is not null
                || format.HighAnsiFont is not null
                || format.EastAsiaFont is not null
                || format.ComplexScriptFont is not null)
            {
                properties.RunFonts ??= new RunFonts();
                if (format.AsciiFont is not null)
                {
                    properties.RunFonts.Ascii = format.AsciiFont;
                }

                if (format.HighAnsiFont is not null)
                {
                    properties.RunFonts.HighAnsi = format.HighAnsiFont;
                }

                if (format.EastAsiaFont is not null)
                {
                    properties.RunFonts.EastAsia = format.EastAsiaFont;
                }

                if (format.ComplexScriptFont is not null)
                {
                    properties.RunFonts.ComplexScript = format.ComplexScriptFont;
                }
            }

            if (format.Bold is not null)
            {
                properties.Bold ??= new Bold();
                properties.Bold.Val = format.Bold.Value;
            }

            if (format.Italic is not null)
            {
                properties.Italic ??= new Italic();
                properties.Italic.Val = format.Italic.Value;
            }

            if (format.FontSizeHalfPoints is not null)
            {
                properties.FontSize ??= new FontSize();
                properties.FontSize.Val = format.FontSizeHalfPoints;
            }
        }
    }

    private static void ApplyTableFormat(Table table, TableFormatSample format)
    {
        var properties = GetOrCreateTableProperties(table);

        if (format.WidthTwips is not null || format.WidthType is not null)
        {
            properties.TableWidth ??= new TableWidth();
            if (format.WidthTwips is not null)
            {
                SetWordprocessingAttribute(properties.TableWidth, "w", format.WidthTwips.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(format.WidthType))
            {
                SetWordprocessingAttribute(properties.TableWidth, "type", format.WidthType);
            }
        }

        if (!string.IsNullOrWhiteSpace(format.Alignment))
        {
            properties.TableJustification ??= new TableJustification();
            SetWordprocessingAttribute(properties.TableJustification, "val", format.Alignment);
        }

        if (format.Borders is not null)
        {
            ApplyTableBorders(properties, format.Borders);
        }

        if (format.CellMargins is not null)
        {
            ApplyTableCellMargins(properties, format.CellMargins);
        }

        if (format.GridColumnWidthsTwips.Count > 0)
        {
            ApplyTableGrid(table, format.GridColumnWidthsTwips);
        }

        ApplyTableHeaderRows(table, format.HeaderRowCount);

        if (format.FirstCellParagraphFormat is not null)
        {
            var firstCellParagraph = table
                .Elements<TableRow>()
                .SelectMany(row => row.Elements<TableCell>())
                .SelectMany(cell => cell.Elements<Paragraph>())
                .FirstOrDefault();
            if (firstCellParagraph is not null)
            {
                ApplyParagraphFormat(firstCellParagraph, format.FirstCellParagraphFormat);
            }
        }
    }

    private static void ApplyTableBorders(TableProperties properties, TableBordersSample borders)
    {
        var merged = MergeTableBorders(ReadTableBorders(properties.TableBorders), borders);
        properties.TableBorders?.Remove();
        if (merged is null)
        {
            return;
        }

        var tableBorders = new TableBorders();
        ApplyBorderLine(tableBorders, merged.Top, () => new TopBorder());
        ApplyBorderLine(tableBorders, merged.Left, () => new LeftBorder());
        ApplyBorderLine(tableBorders, merged.Bottom, () => new BottomBorder());
        ApplyBorderLine(tableBorders, merged.Right, () => new RightBorder());
        ApplyBorderLine(tableBorders, merged.InsideHorizontal, () => new InsideHorizontalBorder());
        ApplyBorderLine(tableBorders, merged.InsideVertical, () => new InsideVerticalBorder());
        properties.TableBorders = tableBorders;
    }

    private static void ApplyBorderLine<T>(TableBorders borders, TableBorderLineSample? sample, Func<T> create)
        where T : OpenXmlElement
    {
        if (sample is null)
        {
            return;
        }

        var existing = borders.Elements<T>().FirstOrDefault();
        if (existing is null)
        {
            existing = create();
            borders.AppendChild(existing);
        }

        if (!string.IsNullOrWhiteSpace(sample.Value))
        {
            SetWordprocessingAttribute(existing, "val", sample.Value);
        }

        if (!string.IsNullOrWhiteSpace(sample.Size))
        {
            SetWordprocessingAttribute(existing, "sz", sample.Size);
        }

        if (!string.IsNullOrWhiteSpace(sample.Color))
        {
            SetWordprocessingAttribute(existing, "color", sample.Color);
        }

        if (!string.IsNullOrWhiteSpace(sample.Space))
        {
            SetWordprocessingAttribute(existing, "space", sample.Space);
        }
    }

    private static void ApplyTableCellMargins(TableProperties properties, TableCellMarginsSample margins)
    {
        var existing = properties.TableCellMarginDefault;
        existing?.Remove();
        var marginDefault = new TableCellMarginDefault();
        if (margins.TopTwips is not null)
        {
            var top = new TopMargin();
            SetWordprocessingAttribute(top, "w", margins.TopTwips.Value.ToString());
            SetWordprocessingAttribute(top, "type", "dxa");
            marginDefault.AppendChild(top);
        }

        if (margins.LeftTwips is not null)
        {
            var left = new TableCellLeftMargin();
            SetWordprocessingAttribute(left, "w", margins.LeftTwips.Value.ToString());
            SetWordprocessingAttribute(left, "type", "dxa");
            marginDefault.AppendChild(left);
        }

        if (margins.BottomTwips is not null)
        {
            var bottom = new BottomMargin();
            SetWordprocessingAttribute(bottom, "w", margins.BottomTwips.Value.ToString());
            SetWordprocessingAttribute(bottom, "type", "dxa");
            marginDefault.AppendChild(bottom);
        }

        if (margins.RightTwips is not null)
        {
            var right = new TableCellRightMargin();
            SetWordprocessingAttribute(right, "w", margins.RightTwips.Value.ToString());
            SetWordprocessingAttribute(right, "type", "dxa");
            marginDefault.AppendChild(right);
        }

        properties.TableCellMarginDefault = marginDefault;
    }

    private static void ApplyTableGrid(Table table, List<int> widths)
    {
        table.TableGrid?.Remove();
        var grid = new TableGrid();
        foreach (var width in widths)
        {
            var column = new GridColumn();
            SetWordprocessingAttribute(column, "w", width.ToString());
            grid.AppendChild(column);
        }

        var properties = table.TableProperties;
        if (properties is not null)
        {
            table.InsertAfter(grid, properties);
        }
        else
        {
            table.PrependChild(grid);
        }
    }

    private static void EnsureTableGrid(Table table)
    {
        if (table.TableGrid is not null)
        {
            return;
        }

        var columnCount = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Count())
            .DefaultIfEmpty(0)
            .Max();
        if (columnCount == 0)
        {
            return;
        }

        ApplyTableGrid(table, Enumerable.Repeat(0, columnCount).ToList());
    }

    private static void ApplyTableHeaderRows(Table table, int headerRowCount)
    {
        var rows = table.Elements<TableRow>().ToList();
        for (var index = 0; index < rows.Count; index++)
        {
            var properties = GetOrCreateTableRowProperties(rows[index]);
            var existing = properties.GetFirstChild<TableHeader>();
            if (index < headerRowCount)
            {
                if (existing is null)
                {
                    properties.AppendChild(new TableHeader());
                }
            }
            else
            {
                existing?.Remove();
            }
        }
    }

    private static void ReplaceTableCellText(TableCell cell, string text)
    {
        var cellProperties = cell.TableCellProperties?.CloneNode(deep: true) as TableCellProperties;
        cell.RemoveAllChildren();
        if (cellProperties is not null)
        {
            cell.AppendChild(cellProperties);
        }

        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));
        cell.AppendChild(paragraph);
    }

    private static void ApplyTableCellFormat(TableCell cell, ParagraphFormatSample format)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();
        if (paragraphs.Count == 0)
        {
            var paragraph = new Paragraph(new Run(new Text("")));
            cell.AppendChild(paragraph);
            paragraphs.Add(paragraph);
        }

        foreach (var paragraph in paragraphs)
        {
            ApplyParagraphFormat(paragraph, format);
        }
    }

    private static void ApplyTableColumnWidth(Table table, int columnIndex, int widthTwips)
    {
        ApplyTableGrid(table, GetMergedGridWidths(table, columnIndex, widthTwips));

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (columnIndex >= cells.Count)
            {
                continue;
            }

            var properties = GetOrCreateTableCellProperties(cells[columnIndex]);
            properties.TableCellWidth ??= new TableCellWidth();
            SetWordprocessingAttribute(properties.TableCellWidth, "w", widthTwips.ToString());
            SetWordprocessingAttribute(properties.TableCellWidth, "type", "dxa");
        }
    }

    private static List<int> GetMergedGridWidths(Table table, int columnIndex, int widthTwips)
    {
        var existing = table.TableGrid?
            .Elements<GridColumn>()
            .Select(column => ToInt(column.Width) ?? 0)
            .ToList() ?? [];
        var columnCount = Math.Max(
            columnIndex + 1,
            table.Elements<TableRow>()
                .Select(row => row.Elements<TableCell>().Count())
                .DefaultIfEmpty(0)
                .Max());

        while (existing.Count < columnCount)
        {
            existing.Add(0);
        }

        existing[columnIndex] = widthTwips;
        return existing;
    }

    private static void SetTableRowHeader(Table table, int rowIndex, bool header)
    {
        var row = table.Elements<TableRow>().ElementAt(rowIndex);
        var properties = GetOrCreateTableRowProperties(row);
        var existing = properties.GetFirstChild<TableHeader>();
        if (header)
        {
            if (existing is null)
            {
                properties.AppendChild(new TableHeader());
            }
        }
        else
        {
            existing?.Remove();
        }
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

    private static string CellFormatPreview(TableCell cell)
    {
        return FormatPreview(ReadFirstCellParagraphFormat(cell));
    }

    private static ParagraphFormatSample ReadFirstCellParagraphFormat(TableCell cell)
    {
        var paragraph = cell.Elements<Paragraph>().FirstOrDefault();
        return paragraph is null ? new ParagraphFormatSample() : ReadParagraphFormat(paragraph);
    }

    private static bool TryResolveTargets(
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        ResolvedTargetKind acceptedKind,
        out List<ResolvedTarget> matches,
        out string reason)
    {
        matches = [];
        reason = "";

        var resolution = context.Resolver.Resolve(operation.Target, options);
        if (!resolution.Success)
        {
            reason = resolution.ErrorCode!;
            return false;
        }

        if (resolution.Matches.Any(match => match.Kind != acceptedKind))
        {
            reason = "target_type_unsupported";
            return false;
        }

        matches = resolution.Matches;
        return true;
    }

    private static void ReplaceParagraphRuns(Paragraph paragraph, string text)
    {
        var firstRunProperties = paragraph.Descendants<Run>()
            .FirstOrDefault()?
            .RunProperties?
            .CloneNode(deep: true) as RunProperties;
        var paragraphContent = paragraph.ChildElements
            .Where(child => child is not ParagraphProperties)
            .ToList();
        foreach (var child in paragraphContent)
        {
            child.Remove();
        }

        var replacement = new Run();
        if (firstRunProperties is not null)
        {
            replacement.AppendChild(firstRunProperties);
        }

        replacement.AppendChild(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        });
        paragraph.AppendChild(replacement);
    }

    private static ParagraphProperties GetOrCreateParagraphProperties(Paragraph paragraph)
    {
        if (paragraph.ParagraphProperties is not null)
        {
            return paragraph.ParagraphProperties;
        }

        var properties = new ParagraphProperties();
        paragraph.PrependChild(properties);
        return properties;
    }

    private static RunProperties GetOrCreateRunProperties(Run run)
    {
        if (run.RunProperties is not null)
        {
            return run.RunProperties;
        }

        var properties = new RunProperties();
        run.PrependChild(properties);
        return properties;
    }

    private static TableProperties GetOrCreateTableProperties(Table table)
    {
        if (table.TableProperties is not null)
        {
            return table.TableProperties;
        }

        var properties = new TableProperties();
        table.PrependChild(properties);
        return properties;
    }

    private static TableRowProperties GetOrCreateTableRowProperties(TableRow row)
    {
        if (row.TableRowProperties is not null)
        {
            return row.TableRowProperties;
        }

        var properties = new TableRowProperties();
        row.PrependChild(properties);
        return properties;
    }

    private static TableCellProperties GetOrCreateTableCellProperties(TableCell cell)
    {
        if (cell.TableCellProperties is not null)
        {
            return cell.TableCellProperties;
        }

        var properties = new TableCellProperties();
        cell.PrependChild(properties);
        return properties;
    }

    private static bool ApplyBooleanRunProperty<T>(
        RunProperties properties,
        JsonNode? format,
        string propertyName,
        Func<T> create,
        T? existing,
        out string error)
        where T : OpenXmlElement
    {
        error = "";
        var value = GetBool(format, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is null)
        {
            return true;
        }

        if (value.Value)
        {
            if (existing is null)
            {
                properties.AppendChild(create());
            }
        }
        else
        {
            existing?.Remove();
        }

        return true;
    }

    private static void MarkAppliedOperationsAsPreview(DocumentEditResult result)
    {
        foreach (var operation in result.Operations.Where(operation => operation.Status == "applied"))
        {
            operation.Status = "preview";
        }
    }

    private static OperationResult OperationSuccess(ThesisOperation operation, string status)
    {
        return new OperationResult
        {
            Id = operation.Id,
            Status = status
        };
    }

    private static OperationResult OperationError(ThesisOperation operation, string reason)
    {
        return new OperationResult
        {
            Id = operation.Id,
            Status = "error",
            Reason = reason
        };
    }

    private static DocumentEditResult Error(string code, string message, string path)
    {
        return new DocumentEditResult
        {
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = code,
                    Message = message,
                    Path = path
                }
            ]
        };
    }

    private static HashSet<string> ReadParagraphStyles(MainDocumentPart mainPart)
    {
        return mainPart.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .Where(style => string.Equals(style.Type?.InnerText, "paragraph", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(style.StyleId?.Value))
            .Select(style => style.StyleId!.Value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    private static Dictionary<string, int> ReadStyleOutlineLevels(MainDocumentPart mainPart)
    {
        return mainPart.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .Select(style => new
            {
                StyleId = style.StyleId?.Value,
                OutlineLevel = style.GetFirstChild<StyleParagraphProperties>()?.GetFirstChild<OutlineLevel>()?.Val?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.StyleId) && item.OutlineLevel is not null)
            .ToDictionary(item => item.StyleId!, item => item.OutlineLevel!.Value, StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    private static bool HasUnsupportedParagraphContent(Paragraph paragraph)
    {
        return paragraph.ChildElements.Any(child => child is not ParagraphProperties and not Run);
    }

    private static string? GetString(JsonNode? node, string propertyName)
    {
        return GetString(node, propertyName, out _);
    }

    private static string? GetString(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static bool? GetBool(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<bool>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static int? GetInt(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static bool TryGetJsonValue<T>(JsonNode? node, out T value)
    {
        value = default!;
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ParagraphFormatSample Clone(ParagraphFormatSample value)
    {
        return new ParagraphFormatSample
        {
            StyleId = value.StyleId,
            Alignment = value.Alignment,
            SpacingBeforeTwips = value.SpacingBeforeTwips,
            SpacingAfterTwips = value.SpacingAfterTwips,
            LineSpacing = value.LineSpacing,
            LineSpacingRule = value.LineSpacingRule,
            FirstLineIndentTwips = value.FirstLineIndentTwips,
            LeftIndentTwips = value.LeftIndentTwips,
            RightIndentTwips = value.RightIndentTwips,
            RunFormat = Clone(value.RunFormat)
        };
    }

    private static RunFormatSample? Clone(RunFormatSample? value)
    {
        return value is null
            ? null
            : new RunFormatSample
            {
                Bold = value.Bold,
                Italic = value.Italic,
                FontSizeHalfPoints = value.FontSizeHalfPoints,
                AsciiFont = value.AsciiFont,
                HighAnsiFont = value.HighAnsiFont,
                EastAsiaFont = value.EastAsiaFont,
                ComplexScriptFont = value.ComplexScriptFont
            };
    }

    private static TableFormatSample Clone(TableFormatSample value)
    {
        return new TableFormatSample
        {
            WidthTwips = value.WidthTwips,
            WidthType = value.WidthType,
            Alignment = value.Alignment,
            GridColumnWidthsTwips = [.. value.GridColumnWidthsTwips],
            Borders = Clone(value.Borders),
            CellMargins = Clone(value.CellMargins),
            HeaderRowCount = value.HeaderRowCount,
            FirstCellParagraphFormat = value.FirstCellParagraphFormat is null
                ? null
                : Clone(value.FirstCellParagraphFormat)
        };
    }

    private static TableBordersSample? Clone(TableBordersSample? value)
    {
        return value is null
            ? null
            : new TableBordersSample
            {
                Top = Clone(value.Top),
                Bottom = Clone(value.Bottom),
                Left = Clone(value.Left),
                Right = Clone(value.Right),
                InsideHorizontal = Clone(value.InsideHorizontal),
                InsideVertical = Clone(value.InsideVertical)
            };
    }

    private static TableBorderLineSample? Clone(TableBorderLineSample? value)
    {
        return value is null
            ? null
            : new TableBorderLineSample
            {
                Value = value.Value,
                Size = value.Size,
                Color = value.Color,
                Space = value.Space
            };
    }

    private static TableCellMarginsSample? Clone(TableCellMarginsSample? value)
    {
        return value is null
            ? null
            : new TableCellMarginsSample
            {
                TopTwips = value.TopTwips,
                RightTwips = value.RightTwips,
                BottomTwips = value.BottomTwips,
                LeftTwips = value.LeftTwips
            };
    }

    private static ParagraphFormatSample ReadParagraphFormat(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var runProperties = paragraph.Descendants<Run>().FirstOrDefault(run => run.Descendants<Text>().Any())?.RunProperties;
        return new ParagraphFormatSample
        {
            StyleId = properties?.ParagraphStyleId?.Val?.Value,
            Alignment = properties?.Justification?.Val?.InnerText,
            SpacingBeforeTwips = ToInt(properties?.SpacingBetweenLines?.Before),
            SpacingAfterTwips = ToInt(properties?.SpacingBetweenLines?.After),
            LineSpacing = properties?.SpacingBetweenLines?.Line?.Value,
            LineSpacingRule = properties?.SpacingBetweenLines?.LineRule?.InnerText,
            FirstLineIndentTwips = ToInt(properties?.Indentation?.FirstLine),
            LeftIndentTwips = ToInt(properties?.Indentation?.Left),
            RightIndentTwips = ToInt(properties?.Indentation?.Right),
            RunFormat = runProperties is null
                ? null
                : new RunFormatSample
                {
                    Bold = ReadOnOffValue(runProperties.Bold),
                    Italic = ReadOnOffValue(runProperties.Italic),
                    FontSizeHalfPoints = runProperties.FontSize?.Val?.Value,
                    AsciiFont = runProperties.RunFonts?.Ascii?.Value,
                    HighAnsiFont = runProperties.RunFonts?.HighAnsi?.Value,
                    EastAsiaFont = runProperties.RunFonts?.EastAsia?.Value,
                    ComplexScriptFont = runProperties.RunFonts?.ComplexScript?.Value
                }
        };
    }

    private static TableFormatSample ReadTableFormat(Table table)
    {
        var properties = table.TableProperties;
        var width = properties?.TableWidth;
        var firstCellParagraph = table
            .Elements<TableRow>()
            .SelectMany(row => row.Elements<TableCell>())
            .SelectMany(cell => cell.Elements<Paragraph>())
            .FirstOrDefault();

        return new TableFormatSample
        {
            WidthTwips = ToInt(GetWordprocessingAttribute(width, "w")),
            WidthType = GetWordprocessingAttribute(width, "type"),
            Alignment = properties?.TableJustification?.Val?.InnerText,
            GridColumnWidthsTwips = [.. table.TableGrid?
                .Elements<GridColumn>()
                .Select(column => ToInt(column.Width))
                .OfType<int>() ?? []],
            Borders = ReadTableBorders(properties?.TableBorders),
            CellMargins = ReadTableCellMargins(properties?.TableCellMarginDefault),
            HeaderRowCount = table.Elements<TableRow>().Count(row => row.TableRowProperties?.GetFirstChild<TableHeader>() is not null),
            FirstCellParagraphFormat = firstCellParagraph is null ? null : ReadParagraphFormat(firstCellParagraph)
        };
    }

    private static TableBordersSample? ReadTableBorders(TableBorders? borders)
    {
        if (borders is null)
        {
            return null;
        }

        return new TableBordersSample
        {
            Top = ReadBorderLine(borders.TopBorder),
            Bottom = ReadBorderLine(borders.BottomBorder),
            Left = ReadBorderLine(borders.LeftBorder ?? (OpenXmlElement?)borders.StartBorder),
            Right = ReadBorderLine(borders.RightBorder ?? (OpenXmlElement?)borders.EndBorder),
            InsideHorizontal = ReadBorderLine(borders.InsideHorizontalBorder),
            InsideVertical = ReadBorderLine(borders.InsideVerticalBorder)
        };
    }

    private static TableBorderLineSample? ReadBorderLine(OpenXmlElement? border)
    {
        if (border is null)
        {
            return null;
        }

        return new TableBorderLineSample
        {
            Value = GetWordprocessingAttribute(border, "val"),
            Size = GetWordprocessingAttribute(border, "sz"),
            Color = GetWordprocessingAttribute(border, "color"),
            Space = GetWordprocessingAttribute(border, "space")
        };
    }

    private static TableCellMarginsSample? ReadTableCellMargins(TableCellMarginDefault? margins)
    {
        if (margins is null)
        {
            return null;
        }

        return new TableCellMarginsSample
        {
            TopTwips = ToInt(GetWordprocessingAttribute(margins.TopMargin, "w")),
            RightTwips = ToInt(GetWordprocessingAttribute(margins.TableCellRightMargin, "w")
                ?? GetWordprocessingAttribute(margins.EndMargin, "w")),
            BottomTwips = ToInt(GetWordprocessingAttribute(margins.BottomMargin, "w")),
            LeftTwips = ToInt(GetWordprocessingAttribute(margins.TableCellLeftMargin, "w")
                ?? GetWordprocessingAttribute(margins.StartMargin, "w"))
        };
    }

    private static ParagraphFormatSample MergeParagraphFormat(ParagraphFormatSample current, ParagraphFormatSample delta)
    {
        var merged = Clone(current);
        merged.StyleId = delta.StyleId ?? merged.StyleId;
        merged.Alignment = delta.Alignment ?? merged.Alignment;
        merged.SpacingBeforeTwips = delta.SpacingBeforeTwips ?? merged.SpacingBeforeTwips;
        merged.SpacingAfterTwips = delta.SpacingAfterTwips ?? merged.SpacingAfterTwips;
        merged.LineSpacing = delta.LineSpacing ?? merged.LineSpacing;
        merged.LineSpacingRule = delta.LineSpacingRule ?? merged.LineSpacingRule;
        merged.FirstLineIndentTwips = delta.FirstLineIndentTwips ?? merged.FirstLineIndentTwips;
        merged.LeftIndentTwips = delta.LeftIndentTwips ?? merged.LeftIndentTwips;
        merged.RightIndentTwips = delta.RightIndentTwips ?? merged.RightIndentTwips;
        merged.RunFormat = MergeRunFormat(merged.RunFormat, delta.RunFormat);
        return merged;
    }

    private static RunFormatSample? MergeRunFormat(RunFormatSample? current, RunFormatSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new RunFormatSample();
        merged.Bold = delta.Bold ?? merged.Bold;
        merged.Italic = delta.Italic ?? merged.Italic;
        merged.FontSizeHalfPoints = delta.FontSizeHalfPoints ?? merged.FontSizeHalfPoints;
        merged.AsciiFont = delta.AsciiFont ?? merged.AsciiFont;
        merged.HighAnsiFont = delta.HighAnsiFont ?? merged.HighAnsiFont;
        merged.EastAsiaFont = delta.EastAsiaFont ?? merged.EastAsiaFont;
        merged.ComplexScriptFont = delta.ComplexScriptFont ?? merged.ComplexScriptFont;
        return merged;
    }

    private static TableFormatSample MergeTableFormat(TableFormatSample current, TableFormatSample delta)
    {
        var merged = Clone(current);
        merged.WidthTwips = delta.WidthTwips ?? merged.WidthTwips;
        merged.WidthType = delta.WidthType ?? merged.WidthType;
        merged.Alignment = delta.Alignment ?? merged.Alignment;
        if (delta.GridColumnWidthsTwips.Count > 0)
        {
            merged.GridColumnWidthsTwips = [.. delta.GridColumnWidthsTwips];
        }

        merged.Borders = MergeTableBorders(merged.Borders, delta.Borders);
        merged.CellMargins = MergeTableCellMargins(merged.CellMargins, delta.CellMargins);
        merged.HeaderRowCount = delta.HeaderRowCount;
        merged.FirstCellParagraphFormat = delta.FirstCellParagraphFormat is null
            ? merged.FirstCellParagraphFormat is null ? null : Clone(merged.FirstCellParagraphFormat)
            : MergeParagraphFormat(merged.FirstCellParagraphFormat ?? new ParagraphFormatSample(), delta.FirstCellParagraphFormat);
        return merged;
    }

    private static TableBordersSample? MergeTableBorders(TableBordersSample? current, TableBordersSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableBordersSample();
        merged.Top = MergeBorderLine(merged.Top, delta.Top);
        merged.Bottom = MergeBorderLine(merged.Bottom, delta.Bottom);
        merged.Left = MergeBorderLine(merged.Left, delta.Left);
        merged.Right = MergeBorderLine(merged.Right, delta.Right);
        merged.InsideHorizontal = MergeBorderLine(merged.InsideHorizontal, delta.InsideHorizontal);
        merged.InsideVertical = MergeBorderLine(merged.InsideVertical, delta.InsideVertical);
        return merged;
    }

    private static TableBorderLineSample? MergeBorderLine(TableBorderLineSample? current, TableBorderLineSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableBorderLineSample();
        merged.Value = delta.Value ?? merged.Value;
        merged.Size = delta.Size ?? merged.Size;
        merged.Color = delta.Color ?? merged.Color;
        merged.Space = delta.Space ?? merged.Space;
        return merged;
    }

    private static TableCellMarginsSample? MergeTableCellMargins(TableCellMarginsSample? current, TableCellMarginsSample? delta)
    {
        if (delta is null)
        {
            return Clone(current);
        }

        var merged = Clone(current) ?? new TableCellMarginsSample();
        merged.TopTwips = delta.TopTwips ?? merged.TopTwips;
        merged.RightTwips = delta.RightTwips ?? merged.RightTwips;
        merged.BottomTwips = delta.BottomTwips ?? merged.BottomTwips;
        merged.LeftTwips = delta.LeftTwips ?? merged.LeftTwips;
        return merged;
    }

    private static bool? ReadOnOffValue(OnOffType? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Val is null)
        {
            return true;
        }

        return value.Val.Value;
    }

    private static void SetWordprocessingAttribute(OpenXmlElement element, string localName, string value)
    {
        element.SetAttribute(new OpenXmlAttribute("w", localName, WordprocessingNamespace, value));
    }

    private static string? GetWordprocessingAttribute(OpenXmlElement? element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (string.Equals(attribute.LocalName, localName, StringComparison.Ordinal)
                && string.Equals(attribute.NamespaceUri, WordprocessingNamespace, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attribute.Value) ? null : attribute.Value;
            }
        }

        return null;
    }

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }

    private static bool IsValidHalfPointSize(string value)
    {
        return int.TryParse(value, out var size) && size > 0 && size <= 1638;
    }

    private static bool IsValidAlignment(string? value)
    {
        return NormalizeAlignment(value) is not "\0";
    }

    private static bool IsValidLineSpacingRule(string? value)
    {
        return NormalizeLineSpacingRule(value) is not "\0";
    }

    private static bool IsValidTwips(int? value)
    {
        return value is null or >= 0;
    }

    private static bool TryValidateTableFormat(EditContext context, TableFormatSample format, out string error)
    {
        error = "";
        if (!IsValidTwips(format.WidthTwips)
            || format.GridColumnWidthsTwips.Any(width => !IsValidTwips(width))
            || format.HeaderRowCount < 0
            || !IsValidTableWidthType(format.WidthType)
            || !IsValidAlignment(format.Alignment)
            || !IsValidTableCellMargins(format.CellMargins)
            || !IsValidTableBorders(format.Borders))
        {
            error = "format_value_invalid";
            return false;
        }

        if (format.FirstCellParagraphFormat is not null
            && !TryValidateParagraphFormat(context, format.FirstCellParagraphFormat, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateParagraphFormat(EditContext context, ParagraphFormatSample format, out string error)
    {
        error = "";
        if (!string.IsNullOrWhiteSpace(format.StyleId) && !context.ParagraphStyleIds.Contains(format.StyleId))
        {
            error = "paragraph_style_missing";
            return false;
        }

        if (format.RunFormat?.FontSizeHalfPoints is not null && !IsValidHalfPointSize(format.RunFormat.FontSizeHalfPoints))
        {
            error = "font_size_invalid";
            return false;
        }

        if (!IsValidAlignment(format.Alignment)
            || !IsValidLineSpacingRule(format.LineSpacingRule)
            || !IsValidTwips(format.SpacingBeforeTwips)
            || !IsValidTwips(format.SpacingAfterTwips)
            || !IsValidTwips(format.FirstLineIndentTwips)
            || !IsValidTwips(format.LeftIndentTwips)
            || !IsValidTwips(format.RightIndentTwips))
        {
            error = "format_value_invalid";
            return false;
        }

        return true;
    }

    private static bool IsValidTableCellMargins(TableCellMarginsSample? margins)
    {
        return margins is null
            || (IsValidTwips(margins.TopTwips)
                && IsValidTwips(margins.RightTwips)
                && IsValidTwips(margins.BottomTwips)
                && IsValidTwips(margins.LeftTwips));
    }

    private static bool IsValidTableBorders(TableBordersSample? borders)
    {
        return borders is null
            || (IsValidTableBorderLine(borders.Top)
                && IsValidTableBorderLine(borders.Bottom)
                && IsValidTableBorderLine(borders.Left)
                && IsValidTableBorderLine(borders.Right)
                && IsValidTableBorderLine(borders.InsideHorizontal)
                && IsValidTableBorderLine(borders.InsideVertical));
    }

    private static bool IsValidTableBorderLine(TableBorderLineSample? line)
    {
        return line is null
            || (IsValidBorderString(line.Value)
                && IsValidBorderUInt(line.Size)
                && IsValidBorderColor(line.Color)
                && IsValidBorderUInt(line.Space));
    }

    private static bool IsValidBorderString(string? value)
    {
        return value is null || !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsValidBorderUInt(string? value)
    {
        return value is null || uint.TryParse(value, out _);
    }

    private static bool IsValidBorderColor(string? value)
    {
        return value is null
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || (value.Length == 6 && value.All(Uri.IsHexDigit));
    }

    private static bool IsValidTableWidthType(string? value)
    {
        return NormalizeTableWidthType(value) is not "\0";
    }

    private static string? NormalizeAlignment(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "left" => "left",
            "center" => "center",
            "right" => "right",
            "both" => "both",
            "distribute" => "distribute",
            "mediumkashida" => "mediumKashida",
            "numtab" => "numTab",
            "highkashida" => "highKashida",
            "lowkashida" => "lowKashida",
            "thaidistribute" => "thaiDistribute",
            _ => "\0"
        };
    }

    private static string? NormalizeTableWidthType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "nil" => "nil",
            "pct" => "pct",
            "dxa" => "dxa",
            "auto" => "auto",
            _ => "\0"
        };
    }

    private static string? NormalizeLineSpacingRule(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "auto" => "auto",
            "exact" => "exact",
            "atleast" => "atLeast",
            _ => "\0"
        };
    }

    private static Diagnostic? ValidatePackage(string path, HashSet<string> baselineErrors)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var firstNewError = new OpenXmlValidator()
            .Validate(document)
            .FirstOrDefault(error => !baselineErrors.Contains(ValidationSignature(error)));
        if (firstNewError is null)
        {
            return null;
        }

        return new Diagnostic
        {
            Severity = "error",
            Code = "document_validation_failed",
            Message = $"Edited document failed OpenXML validation: {firstNewError.Description}",
            Path = Path.GetFullPath(path)
        };
    }

    private static HashSet<string> GetValidationErrors(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        return new OpenXmlValidator()
            .Validate(document)
            .Select(ValidationSignature)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ValidationSignature(ValidationErrorInfo error)
    {
        return $"{error.Path?.XPath}|{error.Description}";
    }

    private static bool HasError(DocumentEditResult result)
    {
        return result.Diagnostics.Any(diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase))
            || result.Operations.Any(operation => operation.Status == "error");
    }

    private static bool HasAppliedOperation(DocumentEditResult result)
    {
        return result.Operations.Any(operation => string.Equals(operation.Status, "applied", StringComparison.OrdinalIgnoreCase));
    }

    private static string RunPreview(Run run)
    {
        var properties = run.RunProperties;
        return $"text={Preview(run.InnerText)};bold={properties?.Bold is not null};italic={properties?.Italic is not null};fontSizeHalfPoints={properties?.FontSize?.Val?.Value}";
    }

    private static string FormatPreview(JsonNode? format)
    {
        return format?.ToJsonString(ThesisJson.Options) ?? "{}";
    }

    private static string FormatPreview(ParagraphFormatSample format)
    {
        return ThesisJson.Serialize(format);
    }

    private static string FormatPreview(TableFormatSample format)
    {
        return ThesisJson.Serialize(format);
    }

    private static string ParagraphFormatPreview(Paragraph paragraph)
    {
        return FormatPreview(ReadParagraphFormat(paragraph));
    }

    private static string TableFormatPreview(Table table)
    {
        return FormatPreview(ReadTableFormat(table));
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int? ToInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string Preview(string text)
    {
        return text.Length <= PreviewLimit ? text : text[..PreviewLimit];
    }

    private static bool IsExpectedEditFailure(Exception ex)
    {
        return ex is InvalidDataException
            or FileFormatException
            or OpenXmlPackageException
            or IOException
            or UnauthorizedAccessException;
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class EditContext(
        HashSet<string> paragraphStyleIds,
        OpenXmlTargetResolver resolver,
        TemplateProfile? profile,
        JsonObject? profileOverrides)
    {
        public HashSet<string> ParagraphStyleIds { get; } = paragraphStyleIds;

        public OpenXmlTargetResolver Resolver { get; } = resolver;

        public TemplateProfile? Profile { get; } = profile;

        public JsonObject? ProfileOverrides { get; } = profileOverrides;
    }
}
