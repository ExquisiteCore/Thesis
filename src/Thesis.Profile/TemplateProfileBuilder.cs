using Thesis.Schema;

namespace Thesis.Profile;

public static class TemplateProfileBuilder
{
    public static TemplateProfile Build(DocumentMap map, string sourceType)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        return new TemplateProfile
        {
            SourceType = sourceType,
            SourceDocument = map.Path,
            RequiresFinalization = map.RequiresFinalization,
            FinalizationReasons = [.. map.FinalizationReasons],
            PageSetup = BuildPageSetup(map),
            StyleRoles = BuildStyleRoles(map),
            NumberingPolicy = BuildNumberingPolicy(map),
            TablePolicy = BuildTablePolicy(map),
            SourceEvidence = BuildSourceEvidence(map)
        };
    }

    private static ProfilePageSetup BuildPageSetup(DocumentMap map)
    {
        var section = map.Sections.FirstOrDefault();
        return new ProfilePageSetup
        {
            PageSize = section?.PageSize is null ? null : Clone(section.PageSize),
            Margins = section?.PageMargin is null ? null : Clone(section.PageMargin),
            Headers = section is null ? [] : [.. section.Headers.Select(Clone)],
            Footers = section is null ? [] : [.. section.Footers.Select(Clone)]
        };
    }

    private static List<ProfileStyleRole> BuildStyleRoles(DocumentMap map)
    {
        var roles = new List<ProfileStyleRole>();
        AddStyleRole(roles, map, "title", "Title");
        AddStyleRole(roles, map, "heading1", "Heading1");
        AddStyleRole(roles, map, "normal", "Normal");
        AddStyleRole(roles, map, "body", "Normal");
        AddSemanticRole(roles, map, "abstract.zh", IsChineseAbstractHeading);
        AddSemanticRole(roles, map, "abstract.en", IsEnglishAbstractHeading);
        AddSemanticRole(roles, map, "toc", IsTocHeading);
        AddSemanticRole(roles, map, "references", IsReferencesHeading);
        return roles;
    }

    private static void AddStyleRole(List<ProfileStyleRole> roles, DocumentMap map, string role, string styleId)
    {
        var style = map.Styles.FirstOrDefault(candidate =>
            string.Equals(candidate.StyleId, styleId, StringComparison.OrdinalIgnoreCase));
        if (style is null)
        {
            return;
        }

        var evidence = map.Paragraphs
            .Where(paragraph => string.Equals(paragraph.StyleId, style.StyleId, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(ToParagraphEvidence)
            .ToList();

        roles.Add(new ProfileStyleRole
        {
            Role = role,
            StyleId = style.StyleId,
            Name = style.Name,
            Type = style.Type,
            BasedOn = style.BasedOn,
            Confidence = evidence.Count > 0 ? 0.9 : 0.55,
            Format = Clone(SelectRoleFormat(map, evidence, style.StyleId)),
            Evidence = evidence
        });
    }

    private static void AddSemanticRole(
        List<ProfileStyleRole> roles,
        DocumentMap map,
        string role,
        Func<string, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(candidate => predicate(candidate.Text));
        if (paragraph is null || string.IsNullOrWhiteSpace(paragraph.StyleId))
        {
            return;
        }

        var style = map.Styles.FirstOrDefault(candidate =>
            string.Equals(candidate.StyleId, paragraph.StyleId, StringComparison.OrdinalIgnoreCase));
        var evidence = new List<ProfileParagraphEvidence> { ToParagraphEvidence(paragraph) };

        roles.Add(new ProfileStyleRole
        {
            Role = role,
            StyleId = paragraph.StyleId,
            Name = style?.Name,
            Type = style?.Type,
            BasedOn = style?.BasedOn,
            Confidence = 0.82,
            Format = Clone(SelectRoleFormat(map, evidence, paragraph.StyleId)),
            Evidence = evidence
        });
    }

    private static ParagraphFormatSample? SelectRoleFormat(
        DocumentMap map,
        List<ProfileParagraphEvidence> evidence,
        string? styleId)
    {
        foreach (var item in evidence)
        {
            var paragraph = map.Paragraphs.FirstOrDefault(candidate => candidate.Index == item.ParagraphIndex);
            if (paragraph is not null)
            {
                return paragraph.Format;
            }
        }

        return string.IsNullOrWhiteSpace(styleId)
            ? null
            : map.Paragraphs.FirstOrDefault(paragraph =>
                string.Equals(paragraph.StyleId, styleId, StringComparison.OrdinalIgnoreCase))?.Format;
    }

    private static ProfileNumberingPolicy BuildNumberingPolicy(DocumentMap map)
    {
        return new ProfileNumberingPolicy
        {
            Detected = map.Numbering.Count > 0 || map.Paragraphs.Any(paragraph => paragraph.Numbering is not null),
            Instances = [.. map.Numbering.Select(Clone)],
            ParagraphUses = [.. map.Paragraphs
                .Where(paragraph => paragraph.Numbering is not null)
                .Take(10)
                .Select(paragraph => new ProfileNumberingUse
                {
                    ParagraphIndex = paragraph.Index,
                    NumberingId = paragraph.Numbering!.NumberingId,
                    Level = paragraph.Numbering.Level,
                    TextPreview = Preview(paragraph.Text)
                })]
        };
    }

    private static ProfileTablePolicy BuildTablePolicy(DocumentMap map)
    {
        var firstTable = map.Tables.FirstOrDefault();
        return new ProfileTablePolicy
        {
            Detected = map.Tables.Count > 0,
            TableCount = map.Tables.Count,
            ObservedColumnCounts = [.. map.Tables
                .SelectMany(table => table.CellCounts)
                .Where(count => count > 0)
                .Distinct()
                .OrderBy(count => count)],
            Default = firstTable is null
                ? null
                : new ProfileTableSample
                {
                    RowCount = firstTable.RowCount,
                    CellCounts = [.. firstTable.CellCounts],
                    TextPreview = firstTable.TextPreview,
                    Format = Clone(firstTable.Format)
                }
        };
    }

    private static ProfileSourceEvidence BuildSourceEvidence(DocumentMap map)
    {
        return new ProfileSourceEvidence
        {
            ParagraphCount = map.Paragraphs.Count,
            StyleCount = map.Styles.Count,
            NumberingCount = map.Numbering.Count,
            SectionCount = map.Sections.Count,
            TableCount = map.Tables.Count,
            ParagraphSamples = [.. map.Paragraphs.Take(5).Select(ToParagraphEvidence)]
        };
    }

    private static ProfileParagraphEvidence ToParagraphEvidence(DocumentParagraph paragraph)
    {
        return new ProfileParagraphEvidence
        {
            ParagraphIndex = paragraph.Index,
            StyleId = paragraph.StyleId,
            TextPreview = Preview(paragraph.Text)
        };
    }

    private static bool IsChineseAbstractHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "摘要" or "中文摘要";
    }

    private static bool IsEnglishAbstractHeading(string text)
    {
        return string.Equals(NormalizeHeading(text), "abstract", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTocHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "目录" or "目次" or "contents" or "tableofcontents";
    }

    private static bool IsReferencesHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "参考文献" or "references" or "bibliography";
    }

    private static string NormalizeHeading(string text)
    {
        var normalized = text.Trim()
            .Trim(':', '：')
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\u3000", "", StringComparison.Ordinal);

        while (normalized.Length > 0 && (char.IsDigit(normalized[0]) || normalized[0] is '.' or '．' or '、' or ')' or '）'))
        {
            normalized = normalized[1..];
        }

        return normalized.Trim().ToLowerInvariant();
    }

    private static PageSizeInfo Clone(PageSizeInfo value)
    {
        return new PageSizeInfo
        {
            WidthTwips = value.WidthTwips,
            HeightTwips = value.HeightTwips,
            Orientation = value.Orientation
        };
    }

    private static PageMarginInfo Clone(PageMarginInfo value)
    {
        return new PageMarginInfo
        {
            TopTwips = value.TopTwips,
            RightTwips = value.RightTwips,
            BottomTwips = value.BottomTwips,
            LeftTwips = value.LeftTwips,
            HeaderTwips = value.HeaderTwips,
            FooterTwips = value.FooterTwips,
            GutterTwips = value.GutterTwips
        };
    }

    private static ParagraphFormatSample? Clone(ParagraphFormatSample? value)
    {
        return value is null
            ? null
            : new ParagraphFormatSample
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

    private static TableFormatSample? Clone(TableFormatSample? value)
    {
        return value is null
            ? null
            : new TableFormatSample
            {
                WidthTwips = value.WidthTwips,
                WidthType = value.WidthType,
                Alignment = value.Alignment,
                GridColumnWidthsTwips = [.. value.GridColumnWidthsTwips],
                Borders = Clone(value.Borders),
                CellMargins = Clone(value.CellMargins),
                HeaderRowCount = value.HeaderRowCount,
                FirstCellParagraphFormat = Clone(value.FirstCellParagraphFormat)
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

    private static HeaderFooterReference Clone(HeaderFooterReference value)
    {
        return new HeaderFooterReference
        {
            Type = value.Type,
            RelationshipId = value.RelationshipId
        };
    }

    private static DocumentNumbering Clone(DocumentNumbering value)
    {
        return new DocumentNumbering
        {
            NumberingId = value.NumberingId,
            AbstractNumberingId = value.AbstractNumberingId,
            Levels = [.. value.Levels.Select(level => new DocumentNumberingLevel
            {
                Level = level.Level,
                Format = level.Format,
                Text = level.Text
            })]
        };
    }

    private static string Preview(string text)
    {
        return text.Length <= 80 ? text : text[..80];
    }
}
