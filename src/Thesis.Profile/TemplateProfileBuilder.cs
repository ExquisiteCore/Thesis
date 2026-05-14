using Thesis.Core;
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
            StructurePolicy = BuildStructurePolicy(map),
            StylePolicy = BuildStylePolicy(map),
            PackagePolicy = BuildPackagePolicy(map),
            FieldPolicy = BuildFieldPolicy(map),
            ZonePolicy = BuildZonePolicy(map),
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

    private static ProfileStructurePolicy BuildStructurePolicy(DocumentMap map)
    {
        return new ProfileStructurePolicy
        {
            SectionCount = map.Sections.Count,
            Sections = [.. map.Sections.Select(section => new ProfileSectionSignature
            {
                Index = section.Index,
                HeaderSignature = HeaderFooterSignature(section.Headers),
                FooterSignature = HeaderFooterSignature(section.Footers),
                PageSize = section.PageSize is null ? null : ProfileSampleCloner.Clone(section.PageSize),
                Margins = section.PageMargin is null ? null : ProfileSampleCloner.Clone(section.PageMargin)
            })]
        };
    }

    private static ProfileStylePolicy BuildStylePolicy(DocumentMap map)
    {
        var numericStyleIds = map.Styles
            .Select(style => style.StyleId)
            .Where(styleId => !string.IsNullOrWhiteSpace(styleId) && styleId.All(char.IsAsciiDigit))
            .Select(styleId => styleId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(styleId => styleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProfileStylePolicy
        {
            PreserveNumericStyleIds = numericStyleIds.Count > 0,
            NumericStyleIds = numericStyleIds,
            DisallowedGeneratedStyleIds = []
        };
    }

    private static ProfilePackagePolicy BuildPackagePolicy(DocumentMap map)
    {
        var imageRelationships = map.Package.Relationships
            .Where(relationship => string.Equals(relationship.Type, "image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var allImageTargetsAreRelativeMedia = imageRelationships.Count > 0
            && imageRelationships.All(relationship =>
                string.IsNullOrWhiteSpace(relationship.TargetMode)
                && relationship.Target.StartsWith("media/", StringComparison.Ordinal)
                && !relationship.Target.StartsWith("/", StringComparison.Ordinal));

        return new ProfilePackagePolicy
        {
            ImagePartRoot = allImageTargetsAreRelativeMedia ? "word/media" : "",
            ImageRelationshipTargetMode = allImageTargetsAreRelativeMedia ? "relative" : "",
            ImageCount = map.Package.ImageCount > 0 ? map.Package.ImageCount : null,
            AllowUnresolvedImageReferences = false
        };
    }

    private static ProfileFieldPolicy BuildFieldPolicy(DocumentMap map)
    {
        var hasToc = map.FinalizationReasons.Contains("toc", StringComparer.OrdinalIgnoreCase)
            || map.Package.FieldCodes.Any(field => string.Equals(field.Kind, "TOC", StringComparison.OrdinalIgnoreCase));
        return new ProfileFieldPolicy
        {
            RequiresToc = hasToc,
            AllowTcFields = true
        };
    }

    private static ProfileZonePolicy BuildZonePolicy(DocumentMap map)
    {
        var landmarks = new List<ProfileZoneLandmark>();
        AddZoneLandmark(landmarks, map, "abstract.zh", paragraph => ThesisTextHeuristics.IsChineseAbstractHeading(paragraph.Text));
        AddZoneLandmark(landmarks, map, "toc.title", paragraph => ThesisTextHeuristics.IsTocHeading(paragraph.Text));
        AddZoneLandmark(landmarks, map, "body", paragraph => IsChapterHeading(paragraph.Text));
        AddZoneLandmark(landmarks, map, "references", paragraph => ThesisTextHeuristics.IsReferencesHeading(paragraph.Text));
        AddZoneLandmark(landmarks, map, "acknowledgements", paragraph => ThesisTextHeuristics.IsAcknowledgementsHeading(paragraph.Text));

        return new ProfileZonePolicy
        {
            Landmarks = landmarks,
            ForbiddenFrontMatterHeadings = []
        };
    }

    private static List<ProfileStyleRole> BuildStyleRoles(DocumentMap map)
    {
        var roles = new List<ProfileStyleRole>();
        AddStyleRole(roles, map, "title", "Title");
        AddStyleRole(roles, map, "heading1", "Heading1");
        AddStyleRole(roles, map, "normal", "Normal");
        AddStyleRole(roles, map, "body", "Normal");
        AddSemanticRole(roles, map, "abstract.zh", ThesisTextHeuristics.IsChineseAbstractHeading);
        AddSemanticRole(roles, map, "abstract.en", ThesisTextHeuristics.IsEnglishAbstractHeading);
        AddSemanticRole(roles, map, "toc.title", ThesisTextHeuristics.IsTocHeading);
        AddSemanticRole(roles, map, "references", ThesisTextHeuristics.IsReferencesHeading);
        AddSemanticRole(roles, map, "keywords.zh", ThesisTextHeuristics.IsChineseKeywords);
        AddSemanticRole(roles, map, "keywords.en", ThesisTextHeuristics.IsEnglishKeywords);
        AddSemanticRole(roles, map, "acknowledgements", ThesisTextHeuristics.IsAcknowledgementsHeading);
        AddSemanticRole(roles, map, "appendix", ThesisTextHeuristics.IsAppendixHeading);
        AddSemanticRole(roles, map, "figureCaption", ThesisTextHeuristics.IsFigureCaption);
        AddSemanticRole(roles, map, "tableCaption", ThesisTextHeuristics.IsTableCaption);
        return roles;
    }

    private static List<ProfileRolePolicy> BuildRolePolicies(DocumentMap map)
    {
        var policies = new List<ProfileRolePolicy>();
        AddRolePolicy(policies, map, "title", 120, "Title", []);
        AddRolePolicy(policies, map, "heading1", 100, "Heading1", [0]);
        AddRolePolicy(policies, map, "body", 10, "Normal", []);
        DirectFormatRolePolicyBuilder.AddDirectFormatRolePolicies(policies, map);
        AddSemanticRolePolicy(policies, map, "abstract.zh", 90, ThesisTextHeuristics.IsChineseAbstractHeading);
        AddSemanticRolePolicy(policies, map, "abstract.en", 90, ThesisTextHeuristics.IsEnglishAbstractHeading);
        AddSemanticRolePolicy(policies, map, "toc.title", 80, ThesisTextHeuristics.IsTocHeading);
        AddSemanticRolePolicy(policies, map, "references", 80, ThesisTextHeuristics.IsReferencesHeading);
        AddSemanticRolePolicy(policies, map, "keywords.zh", 70, ThesisTextHeuristics.IsChineseKeywords);
        AddSemanticRolePolicy(policies, map, "keywords.en", 70, ThesisTextHeuristics.IsEnglishKeywords);
        AddSemanticRolePolicy(policies, map, "acknowledgements", 75, ThesisTextHeuristics.IsAcknowledgementsHeading);
        AddSemanticRolePolicy(policies, map, "appendix", 75, ThesisTextHeuristics.IsAppendixHeading);
        AddPatternSemanticRolePolicy(
            policies,
            map,
            "figureCaption",
            65,
            ThesisTextHeuristics.IsFigureCaption,
            @"^(?:图\s*[\d一二三四五六七八九十IVXivx]+[-－\.．]?\d*|Figure\s+\d+(?:[-.]\d+)?)\s+.*$");
        AddPatternSemanticRolePolicy(
            policies,
            map,
            "tableCaption",
            65,
            ThesisTextHeuristics.IsTableCaption,
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
                TextPatterns = [ThesisTextHeuristics.CreateExactTextPattern(paragraph.Text)],
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

    private static void AddZoneLandmark(
        List<ProfileZoneLandmark> landmarks,
        DocumentMap map,
        string role,
        Func<DocumentParagraph, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        landmarks.Add(new ProfileZoneLandmark
        {
            Role = role,
            ParagraphIndex = paragraph.Index,
            BodyElementIndex = paragraph.BodyElementIndex,
            TextPreview = Preview(paragraph.Text)
        });
    }

    private static string HeaderFooterSignature(List<HeaderFooterReference> references)
    {
        return string.Join(
            "|",
            references
                .Select(reference => Lower(reference.Type))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsChapterHeading(DocumentParagraph paragraph)
    {
        return IsChapterHeading(paragraph.Text);
    }

    private static bool IsChapterHeading(string text)
    {
        var normalized = text.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(
            normalized,
            @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?:\s+\S.*)?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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
