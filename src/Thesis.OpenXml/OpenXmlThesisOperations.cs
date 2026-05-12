using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult InsertCaption(
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

    private static OperationResult SetHeaderFooterText(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (operation.Text is null)
        {
            return OperationError(operation, "text_missing");
        }

        var kind = OpenXmlOperationJson.GetString(operation.Format, "kind", out var kindError) ?? "header";
        var type = OpenXmlOperationJson.GetString(operation.Format, "type", out var typeError) ?? "default";
        if (kindError is not null || typeError is not null)
        {
            return OperationError(operation, kindError ?? typeError!);
        }

        if (kind is not "header" and not "footer" || type is not "default" and not "first" and not "even")
        {
            return OperationError(operation, "target_value_invalid");
        }

        if (writeChanges)
        {
            OpenXmlHeaderFooterEditor.SetHeaderFooterText(context.MainPart, context.Body, kind, type, operation.Text);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo
        {
            Id = $"{kind}:{type}",
            Type = kind,
            PreviewAfter = operation.Text
        });
        return result;
    }

    private static OperationResult InsertPageNumber(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        var kind = OpenXmlOperationJson.GetString(operation.Format, "kind", out var kindError) ?? "footer";
        var type = OpenXmlOperationJson.GetString(operation.Format, "type", out var typeError) ?? "default";
        var alignment = OpenXmlOperationJson.GetString(operation.Format, "alignment", out var alignmentError) ?? "center";
        if (kindError is not null || typeError is not null || alignmentError is not null)
        {
            return OperationError(operation, kindError ?? typeError ?? alignmentError!);
        }

        if (kind is not "header" and not "footer"
            || type is not "default" and not "first" and not "even"
            || alignment is not "left" and not "center" and not "right" and not "both")
        {
            return OperationError(operation, "target_value_invalid");
        }

        if (writeChanges)
        {
            OpenXmlHeaderFooterEditor.InsertPageNumber(context.MainPart, context.Body, kind, type, alignment);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo
        {
            Id = $"{kind}:{type}:pageNumber",
            Type = kind,
            PreviewAfter = "PAGE"
        });
        return result;
    }

    private static OperationResult NormalizeReferences(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var position = OpenXmlOperationJson.GetString(operation.Format, "position", out var positionError) ?? "afterHeading";
        if (positionError is not null)
        {
            return OperationError(operation, positionError);
        }

        if (position is not "afterHeading" and not "self")
        {
            return OperationError(operation, "target_value_invalid");
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                var next = target.Paragraph.ElementsAfter().OfType<Paragraph>().FirstOrDefault();
                if (next is null || !next.InnerText.TrimStart().StartsWith("[1]", StringComparison.Ordinal))
                {
                    var reference = OpenXmlParagraphTools.CreateTextParagraph("[1] ");
                    InsertRelativeTo(target.Paragraph, reference, position == "self" ? "before" : "after");
                    context.RefreshResolver();
                }
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, "[1] "));
        }

        return result;
    }
}
