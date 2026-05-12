using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult InsertTocField(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        var levels = OpenXmlOperationJson.GetString(operation.Format, "levels", out var levelsError) ?? "1-3";
        if (levelsError is not null || !Regex.IsMatch(levels, @"^\d+-\d+$", RegexOptions.CultureInvariant))
        {
            return OperationError(operation, levelsError ?? "target_value_invalid");
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

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            if (writeChanges)
            {
                var paragraph = CreateTocParagraph(levels);
                InsertRelativeTo(target.Paragraph, paragraph, position);
                MarkDocumentFieldsDirty(context.MainPart);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(target.Paragraph.InnerText, $"TOC {levels}"));
        }

        return result;
    }

    private static OperationResult MarkTocNeedsUpdate(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        if (writeChanges)
        {
            foreach (var field in context.Body.Descendants<FieldChar>())
            {
                field.Dirty = true;
            }

            foreach (var simpleField in context.Body.Descendants<SimpleField>())
            {
                simpleField.Dirty = true;
            }

            MarkDocumentFieldsDirty(context.MainPart);
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo { Id = "fields", Type = "field", PreviewAfter = "dirty" });
        return result;
    }

    private static OperationResult UpdateSimpleFields(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        var count = context.Body.Descendants<SimpleField>().Count();
        if (writeChanges)
        {
            foreach (var simpleField in context.Body.Descendants<SimpleField>())
            {
                simpleField.Dirty = false;
            }
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo { Id = "simpleFields", Type = "field", PreviewAfter = count.ToString() });
        return result;
    }

    private static OperationResult NormalizeRuns(
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
        foreach (var target in targets.Cast<ResolvedParagraphTarget>())
        {
            var before = target.Paragraph.InnerText;
            if (writeChanges)
            {
                if (HasUnsupportedParagraphContent(target.Paragraph))
                {
                    return OperationError(operation, "paragraph_structure_unsupported");
                }

                ReplaceParagraphRuns(target.Paragraph, before);
                context.RefreshResolver();
            }

            result.Matches.Add(target.ToMatchInfo(before, before));
        }

        return result;
    }

    private static OperationResult RemoveExtraSpaces(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text => Regex.Replace(text, @"[ \t\u3000]{2,}", " ", RegexOptions.CultureInvariant),
            allowNoOp: true);
    }

    private static OperationResult NormalizeChinesePunctuationSpacing(
        OpenXmlEditContext context,
        RunOptions options,
        ThesisOperation operation,
        bool writeChanges)
    {
        return ApplyParagraphTextTransform(
            context,
            options,
            operation,
            writeChanges,
            text =>
            {
                var noSpaceBefore = Regex.Replace(text, @"\s+([，。；：！？、])", "$1", RegexOptions.CultureInvariant);
                return Regex.Replace(noSpaceBefore, @"([，。；：！？、])\s+", "$1", RegexOptions.CultureInvariant);
            },
            allowNoOp: true);
    }

    private static OperationResult RemoveDuplicatePageBreaks(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        var removed = 0;
        var previousWasPageBreak = false;
        foreach (var paragraph in context.Body.Elements<Paragraph>().ToList())
        {
            var isPageBreak = IsPageBreakOnlyParagraph(paragraph);
            if (isPageBreak && previousWasPageBreak)
            {
                removed++;
                if (writeChanges)
                {
                    paragraph.Remove();
                }
            }

            previousWasPageBreak = isPageBreak;
            if (!isPageBreak && !string.IsNullOrWhiteSpace(paragraph.InnerText))
            {
                previousWasPageBreak = false;
            }
        }

        if (writeChanges && removed > 0)
        {
            context.RefreshResolver();
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo { Id = "pageBreaks", Type = "cleanup", PreviewAfter = removed.ToString() });
        return result;
    }

    private static OperationResult EnsureRoleOrder(
        OpenXmlEditContext context,
        ThesisOperation operation,
        bool writeChanges)
    {
        var order = OpenXmlOperationJson.GetStringArray(operation.Format, "order", out var orderError);
        if (orderError is not null || order.Count < 2)
        {
            return OperationError(operation, orderError ?? "target_value_invalid");
        }

        var paragraphs = context.Body.Elements<Paragraph>().ToList();
        var selected = order
            .Select(text => paragraphs.FirstOrDefault(paragraph => string.Equals(paragraph.InnerText, text, StringComparison.Ordinal)))
            .ToList();
        if (selected.Any(paragraph => paragraph is null))
        {
            return OperationError(operation, "target_not_found");
        }

        if (writeChanges)
        {
            var firstSelectedIndex = selected
                .OfType<Paragraph>()
                .Select(paragraph => paragraphs.IndexOf(paragraph))
                .Min();
            OpenXmlElement? insertionAnchor = paragraphs
                .Skip(firstSelectedIndex)
                .FirstOrDefault(paragraph => !selected.Contains(paragraph));
            insertionAnchor ??= context.Body.Elements<SectionProperties>().FirstOrDefault();
            var clones = selected
                .OfType<Paragraph>()
                .Select(paragraph => paragraph.CloneNode(deep: true))
                .ToList();
            foreach (var paragraph in selected.OfType<Paragraph>())
            {
                paragraph.Remove();
            }

            foreach (var clone in clones)
            {
                if (insertionAnchor is null)
                {
                    context.Body.AppendChild(clone);
                }
                else
                {
                    insertionAnchor.InsertBeforeSelf(clone);
                }
            }

            context.RefreshResolver();
        }

        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo { Id = "roleOrder", Type = "cleanup", PreviewAfter = string.Join("|", order) });
        return result;
    }

    private static Paragraph CreateTocParagraph(string levels)
    {
        return new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
            new Run(new FieldCode($" TOC \\o \"{levels}\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("目录待更新")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static void MarkDocumentFieldsDirty(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings ??= new Settings();
        settingsPart.Settings.RemoveAllChildren<UpdateFieldsOnOpen>();
        settingsPart.Settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static bool IsPageBreakOnlyParagraph(Paragraph paragraph)
    {
        return string.IsNullOrWhiteSpace(paragraph.InnerText)
            && paragraph.Descendants<Break>().Any(breakElement => breakElement.Type?.Value == BreakValues.Page);
    }
}
