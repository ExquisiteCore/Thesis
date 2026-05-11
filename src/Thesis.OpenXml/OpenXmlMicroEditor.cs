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

    public static DocumentEditResult Apply(string docxPath, OperationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Operations.Count == 0)
        {
            return new DocumentEditResult();
        }

        if (request.Mode is RequestMode.DryRun or RequestMode.ValidateOnly)
        {
            return Edit(docxPath, request, writeChanges: false);
        }

        var fullPath = Path.GetFullPath(docxPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Document path has no parent directory.");
        var tempPath = Path.Combine(directory, Path.GetFileName(fullPath) + ".run-" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var baselineValidationErrors = GetValidationErrors(fullPath);
            File.Copy(fullPath, tempPath);
            var result = Edit(tempPath, request, writeChanges: true);
            if (HasError(result))
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

    private static DocumentEditResult Edit(string docxPath, OperationRequest request, bool writeChanges)
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

            var context = new EditContext(body, ReadParagraphStyles(mainPart));
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
            else if (writeChanges)
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
            "replaceParagraphText" => ReplaceParagraphText(context, options, operation, writeChanges),
            "setParagraphStyle" => SetParagraphStyle(context, options, operation, writeChanges),
            "setRunFormat" => SetRunFormat(context, operation, writeChanges),
            null or "" => OperationError(operation, "operation_missing"),
            _ => OperationError(operation, "operation_unknown")
        };
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

        if (!TryResolveParagraphs(context, options, operation.Target, out var matches, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var (paragraph, index) in matches)
        {
            var before = paragraph.InnerText;
            if (writeChanges)
            {
                if (HasUnsupportedParagraphContent(paragraph))
                {
                    return OperationError(operation, "paragraph_structure_unsupported");
                }

                ReplaceParagraphRuns(paragraph, operation.Text);
            }

            result.Matches.Add(ParagraphMatch(index, before, operation.Text));
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

        if (!TryResolveParagraphs(context, options, operation.Target, out var matches, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var (paragraph, index) in matches)
        {
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

            result.Matches.Add(ParagraphMatch(index, before ?? "", styleId));
        }

        return result;
    }

    private static OperationResult SetRunFormat(EditContext context, ThesisOperation operation, bool writeChanges)
    {
        if (!TryResolveRun(context, operation.Target, out var run, out var paragraphIndex, out var runIndex, out var reason))
        {
            return OperationError(operation, reason);
        }

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
        result.Matches.Add(new MatchInfo
        {
            Id = $"{paragraphIndex}:{runIndex}",
            Type = "run",
            Preview = Preview(run.InnerText),
            PreviewBefore = before,
            PreviewAfter = FormatPreview(operation.Format)
        });
        return result;
    }

    private static bool TryResolveParagraphs(
        EditContext context,
        RunOptions options,
        JsonNode? target,
        out List<(Paragraph Paragraph, int Index)> matches,
        out string reason)
    {
        matches = [];
        reason = "";

        var type = GetString(target, "type", out var typeError);
        if (typeError is not null)
        {
            reason = typeError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            reason = "target_type_missing";
            return false;
        }

        if (type == "paragraphIndex")
        {
            var index = GetInt(target, "index", out var indexError);
            if (indexError is not null)
            {
                reason = indexError;
                return false;
            }

            if (index is null)
            {
                reason = "paragraph_index_missing";
                return false;
            }

            if (index < 0 || index >= context.Paragraphs.Count)
            {
                reason = "paragraph_not_found";
                return false;
            }

            matches.Add((context.Paragraphs[index.Value], index.Value));
            return true;
        }

        if (type == "paragraphText")
        {
            var text = GetString(target, "text", out var textError);
            if (textError is not null)
            {
                reason = textError;
                return false;
            }

            if (text is null)
            {
                reason = "paragraph_text_missing";
                return false;
            }

            var match = GetString(target, "match", out var matchError) ?? "exact";
            if (matchError is not null)
            {
                reason = matchError;
                return false;
            }

            matches = context.Paragraphs
                .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
                .Where(candidate => ParagraphTextMatches(candidate.Paragraph.InnerText, text, match))
                .ToList();

            if (matches.Count == 0)
            {
                reason = "paragraph_not_found";
                return false;
            }

            if (matches.Count > 1 && options.RequireSingleMatch)
            {
                reason = "paragraph_ambiguous";
                return false;
            }

            return true;
        }

        reason = "target_type_unsupported";
        return false;
    }

    private static bool TryResolveRun(
        EditContext context,
        JsonNode? target,
        out Run run,
        out int paragraphIndex,
        out int runIndex,
        out string reason)
    {
        run = null!;
        paragraphIndex = -1;
        runIndex = -1;
        reason = "";

        var type = GetString(target, "type", out var typeError);
        if (typeError is not null)
        {
            reason = typeError;
            return false;
        }

        if (type != "runIndex")
        {
            reason = "target_type_unsupported";
            return false;
        }

        var requestedParagraphIndex = GetInt(target, "paragraphIndex", out var paragraphIndexError);
        var requestedRunIndex = GetInt(target, "runIndex", out var runIndexError);
        if (paragraphIndexError is not null || runIndexError is not null)
        {
            reason = paragraphIndexError ?? runIndexError!;
            return false;
        }

        if (requestedParagraphIndex is null || requestedRunIndex is null)
        {
            reason = "run_index_missing";
            return false;
        }

        if (requestedParagraphIndex < 0 || requestedParagraphIndex >= context.Paragraphs.Count)
        {
            reason = "paragraph_not_found";
            return false;
        }

        var runs = context.Paragraphs[requestedParagraphIndex.Value]
            .Descendants<Run>()
            .ToList();
        if (requestedRunIndex < 0 || requestedRunIndex >= runs.Count)
        {
            reason = "run_not_found";
            return false;
        }

        paragraphIndex = requestedParagraphIndex.Value;
        runIndex = requestedRunIndex.Value;
        run = runs[runIndex];
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

    private static MatchInfo ParagraphMatch(int index, string before, string after)
    {
        return new MatchInfo
        {
            Id = index.ToString(),
            Type = "paragraph",
            Preview = Preview(before),
            PreviewBefore = Preview(before),
            PreviewAfter = Preview(after)
        };
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

    private static List<Paragraph> SelectIndexedParagraphs(Body body)
    {
        return body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<Table>().Any())
            .Where(paragraph => !IsFieldOnlyParagraph(paragraph))
            .ToList();
    }

    private static bool IsFieldOnlyParagraph(Paragraph paragraph)
    {
        var hasFields = paragraph.Descendants<FieldChar>().Any()
            || paragraph.Descendants<FieldCode>().Any()
            || paragraph.Descendants<SimpleField>().Any();
        return hasFields && !paragraph.Descendants<Text>().Any(text => !string.IsNullOrWhiteSpace(text.Text));
    }

    private static bool HasUnsupportedParagraphContent(Paragraph paragraph)
    {
        return paragraph.ChildElements.Any(child => child is not ParagraphProperties and not Run);
    }

    private static bool ParagraphTextMatches(string candidate, string text, string match)
    {
        return match switch
        {
            "contains" => candidate.Contains(text, StringComparison.Ordinal),
            "exact" => string.Equals(candidate, text, StringComparison.Ordinal),
            _ => string.Equals(candidate, text, StringComparison.Ordinal)
        };
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

    private static int? GetInt(JsonNode? node, string propertyName)
    {
        return GetInt(node, propertyName, out _);
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

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }

    private static bool IsValidHalfPointSize(string value)
    {
        return int.TryParse(value, out var size) && size > 0 && size <= 1638;
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

    private static string RunPreview(Run run)
    {
        var properties = run.RunProperties;
        return $"text={Preview(run.InnerText)};bold={properties?.Bold is not null};italic={properties?.Italic is not null};fontSizeHalfPoints={properties?.FontSize?.Val?.Value}";
    }

    private static string FormatPreview(JsonNode? format)
    {
        return format?.ToJsonString(ThesisJson.Options) ?? "{}";
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

    private sealed class EditContext(Body body, HashSet<string> paragraphStyleIds)
    {
        public List<Paragraph> Paragraphs { get; } = SelectIndexedParagraphs(body);

        public HashSet<string> ParagraphStyleIds { get; } = paragraphStyleIds;
    }
}
