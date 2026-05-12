using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult SetParagraphFormat(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
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

    private static OperationResult CopyParagraphFormat(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var sourceNode = operation.Format?["source"];
        if (sourceNode is null)
        {
            return OperationError(operation, "target_value_invalid");
        }

        var sourceResolution = context.Resolver.Resolve(sourceNode, SingleMatchOptions());
        if (!sourceResolution.Success)
        {
            return OperationError(operation, sourceResolution.ErrorCode!);
        }

        if (sourceResolution.Matches.Single() is not ResolvedParagraphTarget source)
        {
            return OperationError(operation, "target_type_unsupported");
        }

        var format = OpenXmlFormatReader.ReadParagraphFormat(source.Paragraph);
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

            result.Matches.Add(target.ToMatchInfo(before, OpenXmlFormatReader.FormatPreview(format)));
        }

        return result;
    }

    private static OperationResult ClearDirectFormatting(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var scope = OpenXmlOperationJson.GetString(operation.Format, "scope", out var scopeError) ?? "paragraphAndRuns";
        if (scopeError is not null)
        {
            return OperationError(operation, scopeError);
        }

        if (scope is not "paragraph" and not "runs" and not "paragraphAndRuns")
        {
            return OperationError(operation, "target_value_invalid");
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
                OpenXmlParagraphTools.ClearDirectFormatting(target.Paragraph, scope);
            }

            result.Matches.Add(target.ToMatchInfo(before, scope));
        }

        return result;
    }

    private static OperationResult SetPageBreakBefore(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var value = OpenXmlOperationJson.GetBool(operation.Format, "value", out var valueError);
        if (valueError is not null)
        {
            return OperationError(operation, valueError);
        }

        value ??= true;
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var before = target.Paragraph.ParagraphProperties?.PageBreakBefore is not null ? "true" : "false";
            if (writeChanges)
            {
                OpenXmlParagraphTools.ApplyPageBreakBefore(target.Paragraph, value.Value);
            }

            result.Matches.Add(target.ToMatchInfo(before, value.Value ? "true" : "false"));
        }

        return result;
    }
}
