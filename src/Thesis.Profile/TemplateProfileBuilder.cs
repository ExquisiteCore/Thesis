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
            RolePolicies = BuildRolePolicies(map),
            FormatClusters = ProfileFormatClusterBuilder.Build(map),
            NumberingPolicy = BuildNumberingPolicy(map),
            TablePolicy = BuildTablePolicy(map),
            TableArchetypes = BuildTableArchetypes(map),
            Diagnostics = TemplateProfileDiagnosticsBuilder.Build(map),
            SourceEvidence = BuildSourceEvidence(map)
        };
    }

    private static ProfilePageSetup BuildPageSetup(DocumentMap map)
    {
        var section = map.Sections.FirstOrDefault();
        return new ProfilePageSetup
        {
            PageSize = section?.PageSize is null ? null : ProfileSampleCloner.Clone(section.PageSize),
            Margins = section?.PageMargin is null ? null : ProfileSampleCloner.Clone(section.PageMargin),
            Headers = section is null ? [] : [.. section.Headers.Select(ProfileSampleCloner.Clone)],
            Footers = section is null ? [] : [.. section.Footers.Select(ProfileSampleCloner.Clone)]
        };
    }

    private static List<ProfileStyleRole> BuildStyleRoles(DocumentMap map)
    {
        var roles = new List<ProfileStyleRole>();
        AddStyleRole(roles, map, "title", "Title");
        AddStyleRole(roles, map, "heading1", "Heading1");
        AddStyleRole(roles, map, "normal", "Normal");
        AddStyleRole(roles, map, "body", "Normal");
        AddSemanticRole(roles, map, "abstract.zh", ProfileTextHeuristics.IsChineseAbstractHeading);
        AddSemanticRole(roles, map, "abstract.en", ProfileTextHeuristics.IsEnglishAbstractHeading);
        AddSemanticRole(roles, map, "toc", ProfileTextHeuristics.IsTocHeading);
        AddSemanticRole(roles, map, "references", ProfileTextHeuristics.IsReferencesHeading);
        AddSemanticRole(roles, map, "keywords.zh", ProfileTextHeuristics.IsChineseKeywords);
        AddSemanticRole(roles, map, "keywords.en", ProfileTextHeuristics.IsEnglishKeywords);
        AddSemanticRole(roles, map, "acknowledgements", ProfileTextHeuristics.IsAcknowledgementsHeading);
        AddSemanticRole(roles, map, "appendix", ProfileTextHeuristics.IsAppendixHeading);
        AddSemanticRole(roles, map, "figureCaption", ProfileTextHeuristics.IsFigureCaption);
        AddSemanticRole(roles, map, "tableCaption", ProfileTextHeuristics.IsTableCaption);
        return roles;
    }

    private static List<ProfileRolePolicy> BuildRolePolicies(DocumentMap map)
    {
        var policies = new List<ProfileRolePolicy>();
        AddRolePolicy(policies, map, "title", 120, "Title", []);
        AddRolePolicy(policies, map, "heading1", 100, "Heading1", [0]);
        AddRolePolicy(policies, map, "body", 10, "Normal", []);
        DirectFormatRolePolicyBuilder.AddDirectFormatRolePolicies(policies, map);
        AddSemanticRolePolicy(policies, map, "abstract.zh", 90, ProfileTextHeuristics.IsChineseAbstractHeading);
        AddSemanticRolePolicy(policies, map, "abstract.en", 90, ProfileTextHeuristics.IsEnglishAbstractHeading);
        AddSemanticRolePolicy(policies, map, "toc", 80, ProfileTextHeuristics.IsTocHeading);
        AddSemanticRolePolicy(policies, map, "references", 80, ProfileTextHeuristics.IsReferencesHeading);
        AddSemanticRolePolicy(policies, map, "keywords.zh", 70, ProfileTextHeuristics.IsChineseKeywords);
        AddSemanticRolePolicy(policies, map, "keywords.en", 70, ProfileTextHeuristics.IsEnglishKeywords);
        AddSemanticRolePolicy(policies, map, "acknowledgements", 75, ProfileTextHeuristics.IsAcknowledgementsHeading);
        AddSemanticRolePolicy(policies, map, "appendix", 75, ProfileTextHeuristics.IsAppendixHeading);
        AddPatternSemanticRolePolicy(
            policies,
            map,
            "figureCaption",
            65,
            ProfileTextHeuristics.IsFigureCaption,
            @"^(?:图\s*[\d一二三四五六七八九十IVXivx]+[-－\.．]?\d*|Figure\s+\d+(?:[-.]\d+)?)\s+.*$");
        AddPatternSemanticRolePolicy(
            policies,
            map,
            "tableCaption",
            65,
            ProfileTextHeuristics.IsTableCaption,
            @"^(?:表\s*[\d一二三四五六七八九十IVXivx]+[-－\.．]?\d*|Table\s+\d+(?:[-.]\d+)?)\s+.*$");
        return policies;
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
            Format = ProfileSampleCloner.Clone(SelectRoleFormat(map, evidence, style.StyleId)),
            Evidence = evidence
        });
    }

    private static void AddRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        string styleId,
        int[] outlineLevels)
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

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = style.UsageCount > 0 ? 0.88 : 0.55,
            Match = new ProfileRoleMatch
            {
                StyleIds = [style.StyleId ?? styleId],
                OutlineLevels = [.. outlineLevels]
            },
            Format = ProfileSampleCloner.Clone(SelectRoleFormat(map, evidence, style.StyleId))
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
            Format = ProfileSampleCloner.Clone(SelectRoleFormat(map, evidence, paragraph.StyleId)),
            Evidence = evidence
        });
    }

    private static void AddSemanticRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        Func<string, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(candidate => predicate(candidate.Text));
        if (paragraph is null)
        {
            return;
        }

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = 0.82,
            Match = new ProfileRoleMatch
            {
                StyleIds = string.IsNullOrWhiteSpace(paragraph.StyleId) ? [] : [paragraph.StyleId],
                TextPatterns = [ProfileTextHeuristics.CreateExactTextPattern(paragraph.Text)],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : []
            },
            Format = ProfileSampleCloner.Clone(paragraph.Format)
        });
    }

    private static void AddPatternSemanticRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        Func<string, bool> predicate,
        string textPattern)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(candidate => predicate(candidate.Text));
        if (paragraph is null)
        {
            return;
        }

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = 0.78,
            Match = new ProfileRoleMatch
            {
                StyleIds = string.IsNullOrWhiteSpace(paragraph.StyleId) ? [] : [paragraph.StyleId],
                TextPatterns = [textPattern],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : []
            },
            Format = ProfileSampleCloner.Clone(paragraph.Format)
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
            Instances = [.. map.Numbering.Select(ProfileSampleCloner.Clone)],
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
                    Format = ProfileSampleCloner.Clone(firstTable.Format)
                }
        };
    }

    private static List<ProfileTableArchetype> BuildTableArchetypes(DocumentMap map)
    {
        if (map.Tables.Count == 0)
        {
            return [];
        }

        return [.. map.Tables
            .GroupBy(table => TableArchetypeKey(table.Format), StringComparer.Ordinal)
            .Select((group, index) => BuildTableArchetype(group.ToList(), index + 1))
            .OrderByDescending(archetype => archetype.Confidence)
            .ThenBy(archetype => archetype.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static ProfileTableArchetype BuildTableArchetype(IReadOnlyList<DocumentTable> tables, int ordinal)
    {
        var firstTable = tables[0];
        var isThreeLine = IsThreeLineTable(firstTable.Format);
        var isGrid = IsGridTable(firstTable.Format);
        return new ProfileTableArchetype
        {
            Name = isThreeLine ? "threeLine" : isGrid ? "grid" : $"tableFormat{ordinal}",
            Confidence = CalculateTableArchetypeConfidence(isThreeLine, isGrid, tables.Count),
            Match = new ProfileTableMatch
            {
                MinRows = tables.Min(table => table.RowCount),
                MaxRows = tables.Max(table => table.RowCount),
                ColumnCounts = [.. tables
                    .SelectMany(table => table.CellCounts)
                    .Where(count => count > 0)
                    .Distinct()
                    .OrderBy(count => count)]
            },
            Format = ProfileSampleCloner.Clone(firstTable.Format)
        };
    }

    private static bool IsThreeLineTable(TableFormatSample? format)
    {
        var borders = format?.Borders;
        return BorderValueEquals(borders?.Top, "single")
            && BorderValueEquals(borders?.Bottom, "single")
            && BorderValueEquals(borders?.InsideHorizontal, "single")
            && BorderValueEquals(borders?.Left, "nil")
            && BorderValueEquals(borders?.Right, "nil")
            && BorderValueEquals(borders?.InsideVertical, "nil");
    }

    private static bool IsGridTable(TableFormatSample? format)
    {
        var borders = format?.Borders;
        return BorderValueEquals(borders?.Top, "single")
            && BorderValueEquals(borders?.Bottom, "single")
            && BorderValueEquals(borders?.Left, "single")
            && BorderValueEquals(borders?.Right, "single")
            && BorderValueEquals(borders?.InsideHorizontal, "single")
            && BorderValueEquals(borders?.InsideVertical, "single");
    }

    private static double CalculateTableArchetypeConfidence(bool isThreeLine, bool isGrid, int count)
    {
        var baseConfidence = isThreeLine ? 0.82 : isGrid ? 0.76 : 0.62;
        return Math.Min(0.95, baseConfidence + Math.Min(count, 4) * 0.04);
    }

    private static string TableArchetypeKey(TableFormatSample format)
    {
        var borders = format.Borders;
        var margins = format.CellMargins;
        return string.Join("|",
            format.WidthTwips,
            Lower(format.WidthType),
            Lower(format.Alignment),
            string.Join(",", format.GridColumnWidthsTwips),
            BorderKey(borders?.Top),
            BorderKey(borders?.Bottom),
            BorderKey(borders?.Left),
            BorderKey(borders?.Right),
            BorderKey(borders?.InsideHorizontal),
            BorderKey(borders?.InsideVertical),
            margins?.TopTwips,
            margins?.RightTwips,
            margins?.BottomTwips,
            margins?.LeftTwips,
            format.HeaderRowCount,
            ParagraphFormatKey(format.FirstCellParagraphFormat));
    }

    private static string BorderKey(TableBorderLineSample? line)
    {
        return string.Join(":",
            Lower(line?.Value),
            Lower(line?.Size),
            Lower(line?.Color),
            Lower(line?.Space));
    }

    private static string ParagraphFormatKey(ParagraphFormatSample? format)
    {
        if (format is null)
        {
            return "";
        }

        var run = format.RunFormat;
        return string.Join(":",
            Lower(format.StyleId),
            Lower(format.Alignment),
            format.SpacingBeforeTwips,
            format.SpacingAfterTwips,
            Lower(format.LineSpacing),
            Lower(format.LineSpacingRule),
            format.FirstLineIndentTwips,
            format.LeftIndentTwips,
            format.RightIndentTwips,
            run?.Bold,
            run?.Italic,
            Lower(run?.FontSizeHalfPoints),
            Lower(run?.AsciiFont),
            Lower(run?.HighAnsiFont),
            Lower(run?.EastAsiaFont),
            Lower(run?.ComplexScriptFont));
    }

    private static bool BorderValueEquals(TableBorderLineSample? border, string value)
    {
        return string.Equals(border?.Value, value, StringComparison.OrdinalIgnoreCase);
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

    private static string Preview(string text)
    {
        return text.Length <= 80 ? text : text[..80];
    }

    private static string Lower(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.ToLowerInvariant();
    }
}
