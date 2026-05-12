using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult ReplaceText(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        var find = OpenXmlOperationJson.GetString(operation.Format, "find", out var findError);
        if (findError is not null || string.IsNullOrEmpty(find))
        {
            return OperationError(operation, findError ?? "target_value_invalid");
        }

        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text => text.Replace(find, operation.Text, StringComparison.Ordinal));
    }

    private static OperationResult ReplaceRegex(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        var pattern = OpenXmlOperationJson.GetString(operation.Format, "pattern", out var patternError);
        if (patternError is not null || string.IsNullOrWhiteSpace(pattern))
        {
            return OperationError(operation, patternError ?? "target_value_invalid");
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return OperationError(operation, "target_value_invalid");
        }

        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text => regex.Replace(text, operation.Text));
    }

    private static OperationResult InsertTextBefore(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return InsertTextNearMatch(context, options, operation, writeChanges, before: true);
    }

    private static OperationResult InsertTextAfter(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return InsertTextNearMatch(context, options, operation, writeChanges, before: false);
    }

    private static OperationResult DeleteText(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var find = OpenXmlOperationJson.GetString(operation.Format, "find", out var findError);
        if (findError is not null || string.IsNullOrEmpty(find))
        {
            return OperationError(operation, findError ?? "target_value_invalid");
        }

        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text => text.Replace(find, "", StringComparison.Ordinal));
    }

    private static OperationResult InsertTextNearMatch(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges,
        bool before)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        var find = OpenXmlOperationJson.GetString(operation.Format, "find", out var findError);
        if (findError is not null || string.IsNullOrEmpty(find))
        {
            return OperationError(operation, findError ?? "target_value_invalid");
        }

        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text =>
            {
                var index = text.IndexOf(find, StringComparison.Ordinal);
                return index < 0
                    ? text
                    : text.Insert(before ? index : index + find.Length, operation.Text);
            });
    }

    private static OperationResult ApplyParagraphTextTransform(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges,
        Func<string, string> transform)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var paragraph = target.Paragraph;
            var before = paragraph.InnerText;
            var after = transform(before);
            if (after == before)
            {
                return OperationError(operation, "text_not_found");
            }

            if (writeChanges)
            {
                if (HasUnsupportedParagraphContent(paragraph))
                {
                    return OperationError(operation, "paragraph_structure_unsupported");
                }

                ReplaceParagraphRuns(paragraph, after);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, after));
        }

        return result;
    }

    private static void ReplaceParagraphTextPreservingFirstRun(Paragraph paragraph, string text)
    {
        ReplaceParagraphRuns(paragraph, text);
    }
}
