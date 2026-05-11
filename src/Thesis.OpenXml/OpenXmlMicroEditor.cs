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
                new OpenXmlTargetResolver(body, profile, request.ProfileOverrides),
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
        var profileRoles = ProfileRoleResolver.FindRoles(
            context.Profile,
            context.ProfileOverrides,
            operation.Role,
            out var roleError);
        if (roleError is not null)
        {
            return OperationError(operation, roleError);
        }

        var profileFormat = profileRoles.Select(role => role.Format).FirstOrDefault(candidate => candidate is not null);
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

        format.Alignment = NormalizeAlignment(format.Alignment);
        format.LineSpacingRule = NormalizeLineSpacingRule(format.LineSpacingRule);
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

    private static string ParagraphFormatPreview(Paragraph paragraph)
    {
        return FormatPreview(ReadParagraphFormat(paragraph));
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
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
