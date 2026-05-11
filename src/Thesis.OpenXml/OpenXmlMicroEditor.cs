using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class OpenXmlMicroEditor
{
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

        if (size is not null && !OpenXmlOperationFormatBuilder.IsValidHalfPointSize(size))
        {
            return OperationError(operation, "font_size_invalid");
        }

        var before = OpenXmlFormatReader.RunPreview(run);
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
        result.Matches.Add(target.ToMatchInfo(before, OpenXmlFormatReader.FormatPreview(operation.Format)));
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

        if (!OpenXmlOperationFormatBuilder.TryCreateEffectiveFormat(context.ParagraphStyleIds, profileFormat, operation.Format, out var format, out var formatError))
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
            var before = OpenXmlFormatReader.ParagraphFormatPreview(target.Paragraph);
            if (writeChanges)
            {
                OpenXmlFormatApplier.ApplyParagraphFormat(target.Paragraph, format);
            }

            var after = writeChanges
                ? OpenXmlFormatReader.ParagraphFormatPreview(target.Paragraph)
                : OpenXmlFormatReader.FormatPreview(OpenXmlFormatMerger.MergeParagraphFormat(OpenXmlFormatReader.ReadParagraphFormat(target.Paragraph), format));
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

        if (!OpenXmlOperationFormatBuilder.TryCreateEffectiveTableFormat(context.ParagraphStyleIds, profileFormat, operation.Format, out var format, out var formatError))
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
                OpenXmlFormatApplier.ReplaceTableCellText(target.Cell, operation.Text);
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
        EditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var columnIndex = GetInt(operation.Format, "columnIndex", out var columnIndexError);
        var widthTwips = GetInt(operation.Format, "widthTwips", out var widthError);
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

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
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
