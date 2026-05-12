using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
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

            var context = new OpenXmlEditContext(
                mainPart,
                body,
                ReadParagraphStyles(mainPart),
                ReadStyleOutlineLevels(mainPart),
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
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return operation.Op switch
        {
            "resolveTarget" => ResolveTarget(context, options, operation),
            "replaceParagraphText" => ReplaceParagraphText(context, options, operation, writeChanges),
            "setParagraphStyle" => SetParagraphStyle(context, options, operation, writeChanges),
            "setParagraphFormat" => SetParagraphFormat(context, options, operation, writeChanges),
            "copyParagraphFormat" => CopyParagraphFormat(context, options, operation, writeChanges),
            "clearDirectFormatting" => ClearDirectFormatting(context, options, operation, writeChanges),
            "setPageBreakBefore" => SetPageBreakBefore(context, options, operation, writeChanges),
            "setRunFormat" => SetRunFormat(context, operation, writeChanges),
            "insertParagraph" => InsertParagraph(context, options, operation, writeChanges),
            "deleteParagraph" => DeleteParagraph(context, options, operation, writeChanges),
            "moveParagraph" => MoveParagraph(context, options, operation, writeChanges),
            "insertPageBreak" => InsertPageBreak(context, options, operation, writeChanges),
            "addBookmark" => AddBookmark(context, options, operation, writeChanges),
            "addFootnote" => AddFootnote(context, options, operation, writeChanges),
            "applyProfilePageSetup" => ApplyProfilePageSetup(context, operation, writeChanges),
            "applyProfileRole" => ApplyProfileRole(context, options, operation, writeChanges),
            "applyProfileTable" => ApplyProfileTable(context, options, operation, writeChanges),
            "setTableBorders" => SetTableBorders(context, options, operation, writeChanges),
            "setTableCellText" => SetTableCellText(context, options, operation, writeChanges),
            "setTableCellFormat" => SetTableCellFormat(context, options, operation, writeChanges),
            "setTableColumnWidth" => SetTableColumnWidth(context, options, operation, writeChanges),
            "setTableRowHeader" => SetTableRowHeader(context, options, operation, writeChanges),
            "insertTableRow" => InsertTableRow(context, options, operation, writeChanges),
            "deleteTableRow" => DeleteTableRow(context, options, operation, writeChanges),
            "insertTableColumn" => InsertTableColumn(context, options, operation, writeChanges),
            "deleteTableColumn" => DeleteTableColumn(context, options, operation, writeChanges),
            "applyThreeLineTable" => ApplyThreeLineTable(context, options, operation, writeChanges),
            "insertImage" => InsertImage(context, options, operation, writeChanges),
            "insertCaption" => InsertCaption(context, options, operation, writeChanges),
            "setHeaderFooterText" => SetHeaderFooterText(context, operation, writeChanges),
            "insertPageNumber" => InsertPageNumber(context, operation, writeChanges),
            "normalizeReferences" => NormalizeReferences(context, options, operation, writeChanges),
            null or "" => OperationError(operation, "operation_missing"),
            _ => OperationError(operation, "operation_unknown")
        };
    }

    private static OperationResult ResolveTarget(OpenXmlEditContext context, RunOptions options, ThesisOperation operation)
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
        OpenXmlEditContext context,
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
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var styleId = OpenXmlOperationJson.GetString(operation.Format, "styleId", out var formatError);
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

    private static OperationResult SetRunFormat(OpenXmlEditContext context, ThesisOperation operation, bool writeChanges)
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
        var size = OpenXmlOperationJson.GetString(operation.Format, "fontSizeHalfPoints", out var formatError);
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

    private static OperationResult InsertParagraph(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        if (!TryCreateOperationParagraphFormat(context, operation.Format, out var format, out var formatError))
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
            if (writeChanges)
            {
                var paragraph = CreateTextParagraph(operation.Text, format);
                InsertRelativeTo(target.Paragraph, paragraph, position);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, operation.Text));
        }

        return result;
    }

    private static OperationResult DeleteParagraph(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>().OrderByDescending(target => target.ParagraphIndex))
        {
            var before = target.Paragraph.InnerText;
            if (writeChanges)
            {
                target.Paragraph.Remove();
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, ""));
        }

        return result;
    }

    private static OperationResult MoveParagraph(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        var anchorNode = operation.Format?["anchor"];
        if (anchorNode is null)
        {
            return OperationError(operation, "target_value_invalid");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var anchorResolution = context.Resolver.Resolve(anchorNode, SingleMatchOptions());
        if (!anchorResolution.Success)
        {
            return OperationError(operation, anchorResolution.ErrorCode!);
        }

        if (anchorResolution.Matches.Any(match => match.Kind != ResolvedTargetKind.Paragraph))
        {
            return OperationError(operation, "target_type_unsupported");
        }

        var anchor = (ResolvedParagraphTarget)anchorResolution.Matches.Single();
        if (targets.Any(target => ReferenceEquals(((ResolvedParagraphTarget)target).Paragraph, anchor.Paragraph)))
        {
            return OperationError(operation, "target_value_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var before = target.Paragraph.InnerText;
            if (writeChanges)
            {
                target.Paragraph.Remove();
                InsertRelativeTo(anchor.Paragraph, target.Paragraph, position);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, $"{position}:{anchor.Paragraph.InnerText}"));
        }

        return result;
    }

    private static OperationResult InsertPageBreak(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
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
                var paragraph = new Paragraph(new Run(new Break { Type = BreakValues.Page }));
                InsertRelativeTo(target.Paragraph, paragraph, position);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, "pageBreak"));
        }

        return result;
    }

    private static OperationResult AddBookmark(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var name = OpenXmlOperationJson.GetString(operation.Format, "name", out var nameError);
        if (nameError is not null || string.IsNullOrWhiteSpace(name) || !IsValidBookmarkName(name))
        {
            return OperationError(operation, nameError ?? "target_value_invalid");
        }

        if (context.Body.Descendants<BookmarkStart>().Any(bookmark =>
            string.Equals(bookmark.Name?.Value, name, StringComparison.Ordinal)))
        {
            return OperationError(operation, "bookmark_exists");
        }

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        var bookmarkId = NextBookmarkId(context.Body);
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                AddBookmarkToParagraph(target.Paragraph, name, bookmarkId++);
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, name));
        }

        return result;
    }

    private static OperationResult AddFootnote(
        OpenXmlEditContext context,
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
        var nextFootnoteId = writeChanges ? NextFootnoteId(EnsureFootnotesPart(context.MainPart)) : 1;
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                var footnotesPart = EnsureFootnotesPart(context.MainPart);
                var footnoteId = nextFootnoteId++;
                AddFootnoteBody(footnotesPart, footnoteId, operation.Text);
                AddFootnoteReference(target.Paragraph, footnoteId);
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, operation.Text));
        }

        return result;
    }

    private static OperationResult ApplyProfilePageSetup(OpenXmlEditContext context, ThesisOperation operation, bool writeChanges)
    {
        var profileSetup = context.Profile?.PageSetup;
        if (profileSetup is null || (profileSetup.PageSize is null && profileSetup.Margins is null))
        {
            return OperationError(operation, "profile_page_setup_missing");
        }

        if (!TryCreateEffectivePageSetup(profileSetup, operation.Format, out var setup, out var setupError))
        {
            return OperationError(operation, setupError);
        }

        var sectionProperties = GetOrCreateBodySectionProperties(context.Body);
        var before = PageSetupPreview(OpenXmlFormatReaderSection(sectionProperties));
        if (writeChanges)
        {
            ApplyPageSetup(sectionProperties, setup);
        }

        var after = writeChanges
            ? PageSetupPreview(OpenXmlFormatReaderSection(sectionProperties))
            : PageSetupPreview(setup);
        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo
        {
            Id = "section:0",
            Type = "section",
            PreviewBefore = before,
            PreviewAfter = after
        });
        return result;
    }

    private static OperationResult ApplyProfileRole(
        OpenXmlEditContext context,
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

    private static OperationResult InsertImage(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var imagePath = OpenXmlOperationJson.GetString(operation.Format, "imagePath", out var imagePathError);
        if (imagePathError is not null || string.IsNullOrWhiteSpace(imagePath))
        {
            return OperationError(operation, imagePathError ?? "target_value_invalid");
        }

        var widthEmu = OpenXmlOperationJson.GetInt(operation.Format, "widthEmu", out var widthError);
        var heightEmu = OpenXmlOperationJson.GetInt(operation.Format, "heightEmu", out var heightError);
        if (widthError is not null || heightError is not null || widthEmu is null || heightEmu is null || widthEmu <= 0 || heightEmu <= 0)
        {
            return OperationError(operation, widthError ?? heightError ?? "target_value_invalid");
        }

        var position = OpenXmlOperationJson.GetPosition(operation.Format, defaultValue: "after", out var positionError);
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        var fullImagePath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullImagePath))
        {
            return OperationError(operation, "image_not_found");
        }

        var altText = OpenXmlOperationJson.GetString(operation.Format, "altText", out var altTextError) ?? "";
        if (altTextError is not null)
        {
            return OperationError(operation, altTextError);
        }

        if (!TryCreateOperationParagraphFormat(context, operation.Format, out var format, out var formatError))
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
            if (writeChanges)
            {
                var imagePart = AddImagePart(context.MainPart, fullImagePath);
                var relationshipId = context.MainPart.GetIdOfPart(imagePart);
                var paragraph = CreateImageParagraph(
                    relationshipId,
                    widthEmu.Value,
                    heightEmu.Value,
                    altText,
                    NextDrawingId(context.MainPart),
                    format);
                InsertRelativeTo(target.Paragraph, paragraph, position);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, fullImagePath));
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
        OpenXmlEditContext context,
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

    private static bool TryCreateOperationParagraphFormat(
        OpenXmlEditContext context,
        JsonNode? operationFormat,
        out ParagraphFormatSample format,
        out string error)
    {
        return OpenXmlOperationFormatBuilder.TryCreateEffectiveFormat(
            context.ParagraphStyleIds,
            new ParagraphFormatSample(),
            operationFormat,
            out format,
            out error);
    }

    private static Paragraph CreateTextParagraph(string text, ParagraphFormatSample format)
    {
        var paragraph = new Paragraph(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));
        OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, format);
        return paragraph;
    }

    private static void InsertRelativeTo(Paragraph anchor, Paragraph paragraph, string position)
    {
        if (position == "before")
        {
            anchor.InsertBeforeSelf(paragraph);
        }
        else
        {
            anchor.InsertAfterSelf(paragraph);
        }
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

    private static bool IsValidBookmarkName(string name)
    {
        return name.Length <= 40
            && (char.IsLetter(name[0]) || name[0] == '_')
            && name.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static int NextBookmarkId(Body body)
    {
        return body
            .Descendants<BookmarkStart>()
            .Select(bookmark => bookmark.Id?.Value)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static void AddBookmarkToParagraph(Paragraph paragraph, string name, int id)
    {
        var bookmarkId = id.ToString();
        var start = new BookmarkStart { Name = name, Id = bookmarkId };
        var end = new BookmarkEnd { Id = bookmarkId };
        var firstContent = paragraph.ChildElements.FirstOrDefault(child => child is not ParagraphProperties);
        if (firstContent is null)
        {
            paragraph.AppendChild(start);
            paragraph.AppendChild(end);
            return;
        }

        firstContent.InsertBeforeSelf(start);
        paragraph.AppendChild(end);
    }

    private static FootnotesPart EnsureFootnotesPart(MainDocumentPart mainPart)
    {
        var part = mainPart.FootnotesPart ?? mainPart.AddNewPart<FootnotesPart>();
        if (part.Footnotes is null)
        {
            part.Footnotes = new Footnotes();
        }

        EnsureSpecialFootnote(part.Footnotes, -1, FootnoteEndnoteValues.Separator);
        EnsureSpecialFootnote(part.Footnotes, 0, FootnoteEndnoteValues.ContinuationSeparator);
        part.Footnotes.Save();
        return part;
    }

    private static void EnsureSpecialFootnote(Footnotes footnotes, int id, FootnoteEndnoteValues type)
    {
        if (footnotes.Elements<Footnote>().Any(footnote => footnote.Id?.Value == id))
        {
            return;
        }

        footnotes.AppendChild(new Footnote(
            new Paragraph(new Run(new SeparatorMark())))
        {
            Id = id,
            Type = type
        });
    }

    private static int NextFootnoteId(FootnotesPart part)
    {
        var next = part.Footnotes!
            .Elements<Footnote>()
            .Select(footnote => footnote.Id?.Value ?? 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return checked((int)next);
    }

    private static void AddFootnoteBody(FootnotesPart part, int id, string text)
    {
        part.Footnotes!.AppendChild(new Footnote(
            new Paragraph(
                new Run(new FootnoteReferenceMark()),
                new Run(new Text(text)
                {
                    Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
                })))
        {
            Id = id
        });
        part.Footnotes.Save();
    }

    private static void AddFootnoteReference(Paragraph paragraph, int id)
    {
        paragraph.AppendChild(new Run(new FootnoteReference { Id = id }));
    }

    private static ImagePart AddImagePart(MainDocumentPart mainPart, string imagePath)
    {
        var imagePartType = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".bmp" => ImagePartType.Bmp,
            ".gif" => ImagePartType.Gif,
            ".ico" => ImagePartType.Icon,
            ".jpeg" or ".jpg" => ImagePartType.Jpeg,
            ".png" => ImagePartType.Png,
            ".tif" or ".tiff" => ImagePartType.Tiff,
            _ => throw new InvalidDataException("Unsupported image format.")
        };

        var part = mainPart.AddImagePart(imagePartType);
        using var stream = File.OpenRead(imagePath);
        part.FeedData(stream);
        return part;
    }

    private static Paragraph CreateImageParagraph(
        string relationshipId,
        int widthEmu,
        int heightEmu,
        string altText,
        uint drawingId,
        ParagraphFormatSample format)
    {
        var drawing = new Drawing(
            new WP.Inline(
                new WP.Extent { Cx = widthEmu, Cy = heightEmu },
                new WP.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new WP.DocProperties { Id = drawingId, Name = string.IsNullOrWhiteSpace(altText) ? $"Picture {drawingId}" : altText, Description = altText },
                new WP.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = string.IsNullOrWhiteSpace(altText) ? "Picture" : altText },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0, Y = 0 },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });
        var paragraph = new Paragraph(new Run(drawing));
        OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, format);
        return paragraph;
    }

    private static RunOptions SingleMatchOptions()
    {
        return new RunOptions
        {
            CreateSnapshot = false,
            StopOnError = true,
            RequireSingleMatch = true,
            TrackChanges = false
        };
    }

    private static ProfilePageSetup OpenXmlFormatReaderSection(SectionProperties section)
    {
        var pageSize = section.GetFirstChild<PageSize>();
        var margin = section.GetFirstChild<PageMargin>();
        return new ProfilePageSetup
        {
            PageSize = pageSize is null
                ? null
                : new PageSizeInfo
                {
                    WidthTwips = ToInt(pageSize.Width),
                    HeightTwips = ToInt(pageSize.Height),
                    Orientation = pageSize.Orient?.Value.ToString().ToLowerInvariant()
                },
            Margins = margin is null
                ? null
                : new PageMarginInfo
                {
                    TopTwips = ToInt(margin.Top),
                    RightTwips = ToInt(margin.Right),
                    BottomTwips = ToInt(margin.Bottom),
                    LeftTwips = ToInt(margin.Left),
                    HeaderTwips = ToInt(margin.Header),
                    FooterTwips = ToInt(margin.Footer),
                    GutterTwips = ToInt(margin.Gutter)
                }
        };
    }

    private static bool TryCreateEffectivePageSetup(
        ProfilePageSetup profileSetup,
        JsonNode? overrideFormat,
        out ProfilePageSetup setup,
        out string error)
    {
        setup = new ProfilePageSetup
        {
            PageSize = profileSetup.PageSize is null
                ? null
                : new PageSizeInfo
                {
                    WidthTwips = profileSetup.PageSize.WidthTwips,
                    HeightTwips = profileSetup.PageSize.HeightTwips,
                    Orientation = profileSetup.PageSize.Orientation
                },
            Margins = profileSetup.Margins is null
                ? null
                : new PageMarginInfo
                {
                    TopTwips = profileSetup.Margins.TopTwips,
                    RightTwips = profileSetup.Margins.RightTwips,
                    BottomTwips = profileSetup.Margins.BottomTwips,
                    LeftTwips = profileSetup.Margins.LeftTwips,
                    HeaderTwips = profileSetup.Margins.HeaderTwips,
                    FooterTwips = profileSetup.Margins.FooterTwips,
                    GutterTwips = profileSetup.Margins.GutterTwips
                },
            Headers = [.. profileSetup.Headers],
            Footers = [.. profileSetup.Footers]
        };

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        setup.PageSize ??= new PageSizeInfo();
        setup.Margins ??= new PageMarginInfo();
        if (!ApplyPageSizeIntOverride(overrideFormat, setup.PageSize, "widthTwips", (target, value) => target.WidthTwips = value, out error)
            || !ApplyPageSizeIntOverride(overrideFormat, setup.PageSize, "heightTwips", (target, value) => target.HeightTwips = value, out error)
            || !ApplyPageSizeStringOverride(overrideFormat, setup.PageSize, "orientation", (target, value) => target.Orientation = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "topTwips", (target, value) => target.TopTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "rightTwips", (target, value) => target.RightTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "bottomTwips", (target, value) => target.BottomTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "leftTwips", (target, value) => target.LeftTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "headerTwips", (target, value) => target.HeaderTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "footerTwips", (target, value) => target.FooterTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "gutterTwips", (target, value) => target.GutterTwips = value, out error))
        {
            return false;
        }

        if (!IsValidTwips(setup.PageSize.WidthTwips)
            || !IsValidTwips(setup.PageSize.HeightTwips)
            || !IsValidTwips(setup.Margins.TopTwips)
            || !IsValidTwips(setup.Margins.RightTwips)
            || !IsValidTwips(setup.Margins.BottomTwips)
            || !IsValidTwips(setup.Margins.LeftTwips)
            || !IsValidTwips(setup.Margins.HeaderTwips)
            || !IsValidTwips(setup.Margins.FooterTwips)
            || !IsValidTwips(setup.Margins.GutterTwips))
        {
            error = "format_value_invalid";
            return false;
        }

        setup.PageSize.Orientation = NormalizePageOrientation(setup.PageSize.Orientation);
        if (setup.PageSize.Orientation == "\0")
        {
            error = "format_value_invalid";
            return false;
        }

        error = "";
        return true;
    }

    private static void ApplyPageSetup(SectionProperties section, ProfilePageSetup setup)
    {
        if (setup.PageSize is not null)
        {
            section.GetFirstChild<PageSize>()?.Remove();
            var pageSize = new PageSize();
            if (setup.PageSize.WidthTwips is not null)
            {
                pageSize.Width = (UInt32Value)(uint)setup.PageSize.WidthTwips.Value;
            }

            if (setup.PageSize.HeightTwips is not null)
            {
                pageSize.Height = (UInt32Value)(uint)setup.PageSize.HeightTwips.Value;
            }

            if (!string.IsNullOrWhiteSpace(setup.PageSize.Orientation))
            {
                pageSize.Orient = setup.PageSize.Orientation == "landscape"
                    ? PageOrientationValues.Landscape
                    : PageOrientationValues.Portrait;
            }

            InsertPageSize(section, pageSize);
        }

        if (setup.Margins is not null)
        {
            section.GetFirstChild<PageMargin>()?.Remove();
            var margin = new PageMargin();
            if (setup.Margins.TopTwips is not null)
            {
                margin.Top = setup.Margins.TopTwips.Value;
            }

            if (setup.Margins.RightTwips is not null)
            {
                margin.Right = (UInt32Value)(uint)setup.Margins.RightTwips.Value;
            }

            if (setup.Margins.BottomTwips is not null)
            {
                margin.Bottom = setup.Margins.BottomTwips.Value;
            }

            if (setup.Margins.LeftTwips is not null)
            {
                margin.Left = (UInt32Value)(uint)setup.Margins.LeftTwips.Value;
            }

            if (setup.Margins.HeaderTwips is not null)
            {
                margin.Header = (UInt32Value)(uint)setup.Margins.HeaderTwips.Value;
            }

            if (setup.Margins.FooterTwips is not null)
            {
                margin.Footer = (UInt32Value)(uint)setup.Margins.FooterTwips.Value;
            }

            if (setup.Margins.GutterTwips is not null)
            {
                margin.Gutter = (UInt32Value)(uint)setup.Margins.GutterTwips.Value;
            }

            var pageSize = section.GetFirstChild<PageSize>();
            if (pageSize is not null)
            {
                section.InsertAfter(margin, pageSize);
            }
            else
            {
                section.PrependChild(margin);
            }
        }
    }

    private static void InsertPageSize(SectionProperties section, PageSize pageSize)
    {
        var insertAfter = section
            .ChildElements
            .LastOrDefault(child => child is HeaderReference or FooterReference);
        if (insertAfter is not null)
        {
            section.InsertAfter(pageSize, insertAfter);
        }
        else
        {
            section.PrependChild(pageSize);
        }
    }

    private static SectionProperties GetOrCreateBodySectionProperties(Body body)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault();
        if (section is not null)
        {
            return section;
        }

        section = new SectionProperties();
        body.AppendChild(section);
        return section;
    }

    private static bool ApplyPageSizeIntOverride(
        JsonNode? overrideFormat,
        PageSizeInfo pageSize,
        string propertyName,
        Action<PageSizeInfo, int> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(pageSize, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyPageSizeStringOverride(
        JsonNode? overrideFormat,
        PageSizeInfo pageSize,
        string propertyName,
        Action<PageSizeInfo, string> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(pageSize, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyPageMarginIntOverride(
        JsonNode? overrideFormat,
        PageMarginInfo margins,
        string propertyName,
        Action<PageMarginInfo, int> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(margins, value.Value);
        }

        error = "";
        return true;
    }

    private static string PageSetupPreview(ProfilePageSetup setup)
    {
        return ThesisJson.Serialize(new
        {
            pageSize = setup.PageSize,
            margins = setup.Margins
        });
    }

    private static bool IsValidTwips(int? value)
    {
        return value is null or >= 0;
    }

    private static string? NormalizePageOrientation(string? orientation)
    {
        return orientation?.ToLowerInvariant() switch
        {
            null => null,
            "portrait" => "portrait",
            "landscape" => "landscape",
            _ => "\0"
        };
    }

    private static string? LowerInnerText(OpenXmlElement? element)
    {
        return string.IsNullOrWhiteSpace(element?.InnerText) ? null : element.InnerText.ToLowerInvariant();
    }

    private static uint NextDrawingId(MainDocumentPart mainPart)
    {
        var maxId = mainPart.Document!
            .Descendants<WP.DocProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max();
        return maxId + 1U;
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int? ToInt(UInt32Value? value)
    {
        return value is null ? null : checked((int)value.Value);
    }

    private static int? ToInt(Int32Value? value)
    {
        return value?.Value;
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
        var value = OpenXmlOperationJson.GetBool(format, propertyName, out var valueError);
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
            Message = $"Edited document failed OpenXML validation at {firstNewError.Path?.XPath ?? "unknown path"}: {firstNewError.Description}",
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


}
