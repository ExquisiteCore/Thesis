using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult ReplaceReferences(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var items = OpenXmlOperationJson.GetStringArray(operation.Format, "items", out var itemsError);
        if (itemsError is not null || items.Count == 0)
        {
            return OperationError(operation, itemsError ?? "target_value_invalid");
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
                RemoveFollowingReferenceItems(target.Paragraph);
                var anchor = target.Paragraph;
                for (var index = 0; index < items.Count; index++)
                {
                    var paragraph = CreateReferenceParagraph(index + 1, items[index]);
                    anchor.InsertAfterSelf(paragraph);
                    anchor = paragraph;
                }

                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, string.Join(" | ", items)));
        }

        return result;
    }

    private static OperationResult InsertReferenceItem(
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

        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var nextNumber = NextReferenceNumber(context.Body);
        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                var paragraph = CreateReferenceParagraph(nextNumber++, operation.Text);
                InsertRelativeTo(target.Paragraph, paragraph, position);
                RenumberReferences(context.Body);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, operation.Text));
        }

        return result;
    }

    private static OperationResult ApplyReferenceFormat(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (!TryResolveTargets(context, options, operation, ResolvedTargetKind.Paragraph, out var targets, out var reason))
        {
            return OperationError(operation, reason);
        }

        var format = new ParagraphFormatSample
        {
            FirstLineIndentTwips = 420,
            LeftIndentTwips = 0,
            SpacingBeforeTwips = 0,
            SpacingAfterTwips = 0
        };

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (!IsReferenceParagraphText(target.Paragraph.InnerText))
            {
                continue;
            }

            var before = OpenXmlFormatReader.ParagraphFormatPreview(target.Paragraph);
            if (writeChanges)
            {
                OpenXmlFormatApplier.ApplyParagraphFormat(target.Paragraph, format);
            }

            result.Matches.Add(target.ToMatchInfo(before, OpenXmlFormatReader.FormatPreview(format)));
        }

        if (result.Matches.Count == 0)
        {
            return OperationError(operation, "target_not_found");
        }

        return result;
    }

    private static Paragraph CreateReferenceParagraph(int number, string text)
    {
        return OpenXmlParagraphTools.CreateTextParagraph($"[{number}] {StripReferenceNumber(text)}");
    }

    private static void RemoveFollowingReferenceItems(Paragraph heading)
    {
        var current = heading.NextSibling<Paragraph>();
        while (current is not null && IsReferenceParagraphText(current.InnerText))
        {
            var next = current.NextSibling<Paragraph>();
            current.Remove();
            current = next;
        }
    }

    private static int NextReferenceNumber(Body body)
    {
        return body.Descendants<Paragraph>()
            .Select(paragraph => ReferenceNumberRegex().Match(paragraph.InnerText))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static void RenumberReferences(Body body)
    {
        var number = 1;
        foreach (var paragraph in body.Descendants<Paragraph>().Where(paragraph => IsReferenceParagraphText(paragraph.InnerText)))
        {
            ReplaceParagraphRuns(paragraph, $"[{number++}] {StripReferenceNumber(paragraph.InnerText)}");
        }
    }

    private static bool IsReferenceParagraphText(string text)
    {
        return ReferenceNumberRegex().IsMatch(text);
    }

    private static string StripReferenceNumber(string text)
    {
        return ReferenceNumberRegex().Replace(text, "").TrimStart();
    }

    [GeneratedRegex(@"^\s*\[(\d+)\]\s*")]
    private static partial Regex ReferenceNumberRegex();
}
