using System.Text.RegularExpressions;
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
            NumberingPolicy = BuildNumberingPolicy(map),
            TablePolicy = BuildTablePolicy(map),
            TableArchetypes = BuildTableArchetypes(map),
            Diagnostics = BuildDiagnostics(map),
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

    private static List<ProfileRolePolicy> BuildRolePolicies(DocumentMap map)
    {
        var policies = new List<ProfileRolePolicy>();
        AddRolePolicy(policies, map, "title", 120, "Title", []);
        AddRolePolicy(policies, map, "heading1", 100, "Heading1", [0]);
        AddRolePolicy(policies, map, "body", 10, "Normal", []);
        AddDirectFormatRolePolicies(policies, map);
        AddSemanticRolePolicy(policies, map, "abstract.zh", 90, IsChineseAbstractHeading);
        AddSemanticRolePolicy(policies, map, "abstract.en", 90, IsEnglishAbstractHeading);
        AddSemanticRolePolicy(policies, map, "toc", 80, IsTocHeading);
        AddSemanticRolePolicy(policies, map, "references", 80, IsReferencesHeading);
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
            Format = Clone(SelectRoleFormat(map, evidence, style.StyleId)),
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
            Format = Clone(SelectRoleFormat(map, evidence, style.StyleId))
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
                TextPatterns = [CreateExactTextPattern(paragraph.Text)],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : []
            },
            Format = Clone(paragraph.Format)
        });
    }

    private static void AddDirectFormatRolePolicies(List<ProfileRolePolicy> policies, DocumentMap map)
    {
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading1",
            105,
            0.76,
            IsDirectHeading1,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?![\d.．、])\s*.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading2",
            85,
            0.74,
            IsDirectHeading2,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading3",
            75,
            0.72,
            IsDirectHeading3,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "body",
            15,
            0.68,
            IsDirectBody,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))(?!\s*(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章\b|\d{1,2}\.\d{1,2}|摘要\b|Abstract\b|目录\b|参考文献\b|注：|\d+、|\[序号\])).{8,}$");
    }

    private static void AddDirectFormatRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        double confidence,
        Func<DocumentParagraph, bool> predicate,
        string textPattern)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        if (policies.Any(policy =>
            string.Equals(policy.Role, role, StringComparison.Ordinal)
            && policy.Format is not null
            && IsSamePolicyFormat(policy.Format, paragraph.Format)))
        {
            return;
        }

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = confidence,
            Match = new ProfileRoleMatch
            {
                TextPatterns = [textPattern],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : []
            },
            Format = Clone(NormalizePolicyFormat(paragraph.Format))
        });
    }

    private static ParagraphFormatSample NormalizePolicyFormat(ParagraphFormatSample format)
    {
        var clone = Clone(format) ?? new ParagraphFormatSample();
        clone.StyleId = null;
        if (clone.RunFormat is not null && clone.RunFormat.Bold is null)
        {
            clone.RunFormat.Bold = false;
        }

        return clone;
    }

    private static bool IsSamePolicyFormat(ParagraphFormatSample left, ParagraphFormatSample right)
    {
        return string.Equals(left.StyleId, right.StyleId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Alignment, right.Alignment, StringComparison.OrdinalIgnoreCase)
            && left.SpacingBeforeTwips == right.SpacingBeforeTwips
            && left.SpacingAfterTwips == right.SpacingAfterTwips
            && string.Equals(left.LineSpacing, right.LineSpacing, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.LineSpacingRule, right.LineSpacingRule, StringComparison.OrdinalIgnoreCase)
            && left.FirstLineIndentTwips == right.FirstLineIndentTwips
            && left.LeftIndentTwips == right.LeftIndentTwips
            && left.RightIndentTwips == right.RightIndentTwips
            && IsSameRunFormat(left.RunFormat, right.RunFormat);
    }

    private static bool IsSameRunFormat(RunFormatSample? left, RunFormatSample? right)
    {
        return string.Equals(left?.FontSizeHalfPoints, right?.FontSizeHalfPoints, StringComparison.OrdinalIgnoreCase)
            && left?.Bold == right?.Bold
            && left?.Italic == right?.Italic
            && string.Equals(left?.AsciiFont, right?.AsciiFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.HighAnsiFont, right?.HighAnsiFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.EastAsiaFont, right?.EastAsiaFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.ComplexScriptFont, right?.ComplexScriptFont, StringComparison.OrdinalIgnoreCase);
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

    private static List<ProfileTableArchetype> BuildTableArchetypes(DocumentMap map)
    {
        var firstTable = map.Tables.FirstOrDefault();
        if (firstTable is null)
        {
            return [];
        }

        var isThreeLine = IsThreeLineTable(firstTable.Format);
        return
        [
            new ProfileTableArchetype
            {
                Name = isThreeLine ? "threeLine" : "default",
                Confidence = isThreeLine ? 0.9 : 0.65,
                Match = new ProfileTableMatch
                {
                    MinRows = map.Tables.Min(table => table.RowCount),
                    MaxRows = map.Tables.Max(table => table.RowCount),
                    ColumnCounts = [.. map.Tables
                        .SelectMany(table => table.CellCounts)
                        .Where(count => count > 0)
                        .Distinct()
                        .OrderBy(count => count)]
                },
                Format = Clone(firstTable.Format)
            }
        ];
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

    private static bool BorderValueEquals(TableBorderLineSample? border, string value)
    {
        return string.Equals(border?.Value, value, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ProfileDiagnostic> BuildDiagnostics(DocumentMap map)
    {
        var diagnostics = new List<ProfileDiagnostic>();

        if (!map.Paragraphs.Any(paragraph => IsChineseAbstractHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "Chinese abstract heading was not found.",
                Evidence = ["role:abstract.zh"]
            });
        }

        if (!map.Paragraphs.Any(paragraph => IsReferencesHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "References heading was not found.",
                Evidence = ["role:references"]
            });
        }

        if (map.Tables.Count == 0)
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_table_missing",
                Message = "No table samples were found in the source document.",
                Evidence = ["tables:0"]
            });
        }

        AddDirectFormatDiagnostics(diagnostics, map);
        AddAmbiguousStyleDiagnostics(diagnostics, map);
        return diagnostics;
    }

    private static void AddDirectFormatDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        AddDirectFormatDiagnostic(diagnostics, map, "heading1", IsDirectHeading1);
        AddDirectFormatDiagnostic(diagnostics, map, "heading2", IsDirectHeading2);
        AddDirectFormatDiagnostic(diagnostics, map, "heading3", IsDirectHeading3);
        AddDirectFormatDiagnostic(diagnostics, map, "body", IsDirectBody);
    }

    private static void AddDirectFormatDiagnostic(
        List<ProfileDiagnostic> diagnostics,
        DocumentMap map,
        string role,
        Func<DocumentParagraph, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        diagnostics.Add(new ProfileDiagnostic
        {
            Severity = "info",
            Code = "profile_role_inferred",
            Message = $"{role} policy inferred from paragraph text and direct formatting.",
            Evidence =
            [
                $"role:{role}",
                $"paragraph:{paragraph.Index}",
                $"fontSize:{paragraph.Format.RunFormat?.FontSizeHalfPoints}"
            ]
        });
    }

    private static void AddAmbiguousStyleDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        foreach (var group in map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.StyleId))
            .GroupBy(paragraph => paragraph.StyleId!, StringComparer.OrdinalIgnoreCase))
        {
            var detectedRoles = new List<string>();
            if (group.Any(IsDirectHeading1))
            {
                detectedRoles.Add("heading1");
            }

            if (group.Any(IsDirectHeading2))
            {
                detectedRoles.Add("heading2");
            }

            if (group.Any(IsDirectHeading3))
            {
                detectedRoles.Add("heading3");
            }

            if (group.Any(IsDirectBody))
            {
                detectedRoles.Add("body");
            }

            if (detectedRoles.Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_style_ambiguous",
                Message = "A single paragraph style appears to carry multiple semantic roles; direct-format policies were used instead of style-only matching.",
                Evidence =
                [
                    $"style:{group.Key}",
                    "roles:" + string.Join(",", detectedRoles.Distinct(StringComparer.Ordinal))
                ]
            });
        }
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

    private static bool IsDirectHeading1(DocumentParagraph paragraph)
    {
        if (IsSpecialSemanticHeading(paragraph.Text) || IsLikelyTocLine(paragraph.Text))
        {
            return false;
        }

        return Regex.IsMatch(paragraph.Text.Trim(), @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?![\d.．、])", RegexOptions.CultureInvariant)
            && string.Equals(paragraph.Format.Alignment, "center", StringComparison.OrdinalIgnoreCase)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) >= 30;
    }

    private static bool IsDirectHeading2(DocumentParagraph paragraph)
    {
        return !IsLikelyTocLine(paragraph.Text)
            && Regex.IsMatch(paragraph.Text.Trim(), @"^\d{1,2}\.\d{1,2}(?!\.)\s+", RegexOptions.CultureInvariant)
            && IsLeftOrDefaultAligned(paragraph)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) is >= 23 and <= 26
            && !HasFirstLineIndent(paragraph);
    }

    private static bool IsDirectHeading3(DocumentParagraph paragraph)
    {
        return !IsLikelyTocLine(paragraph.Text)
            && Regex.IsMatch(paragraph.Text.Trim(), @"^\d{1,2}\.\d{1,2}\.\d{1,2}(?!\.)\s+", RegexOptions.CultureInvariant)
            && IsLeftOrDefaultAligned(paragraph)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) is >= 20 and <= 22
            && !HasFirstLineIndent(paragraph);
    }

    private static bool IsDirectBody(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        if (text.Length < 8
            || IsSpecialSemanticHeading(text)
            || IsLikelyTocLine(text)
            || IsDirectHeading1(paragraph)
            || IsDirectHeading2(paragraph)
            || IsDirectHeading3(paragraph)
            || Regex.IsMatch(text, @"^(?:注：|\d+、|\[序号\])", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return paragraph.Format.RunFormat?.Bold != true
            && GetFontSize(paragraph) is >= 20 and <= 22
            && string.Equals(paragraph.Format.LineSpacing, "360", StringComparison.OrdinalIgnoreCase)
            && string.Equals(paragraph.Format.LineSpacingRule, "atleast", StringComparison.OrdinalIgnoreCase)
            && paragraph.Format.FirstLineIndentTwips is >= 360 and <= 560;
    }

    private static bool IsSpecialSemanticHeading(string text)
    {
        return IsChineseAbstractHeading(text)
            || IsEnglishAbstractHeading(text)
            || IsTocHeading(text)
            || IsReferencesHeading(text);
    }

    private static bool IsLikelyTocLine(string text)
    {
        return text.Contains('\t')
            || text.Contains("……", StringComparison.Ordinal)
            || text.Contains("......", StringComparison.Ordinal)
            || Regex.IsMatch(text, @"\.{3,}|\d\s*$", RegexOptions.CultureInvariant)
                && Regex.IsMatch(text, @"^(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章|\d{1,2}\.\d{1,2})", RegexOptions.CultureInvariant);
    }

    private static bool IsLeftOrDefaultAligned(DocumentParagraph paragraph)
    {
        return string.IsNullOrWhiteSpace(paragraph.Format.Alignment)
            || string.Equals(paragraph.Format.Alignment, "left", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFirstLineIndent(DocumentParagraph paragraph)
    {
        return paragraph.Format.FirstLineIndentTwips is not null
            && Math.Abs(paragraph.Format.FirstLineIndentTwips.Value) > 0;
    }

    private static int GetFontSize(DocumentParagraph paragraph)
    {
        return int.TryParse(paragraph.Format.RunFormat?.FontSizeHalfPoints, out var result)
            ? result
            : 0;
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

    private static string CreateExactTextPattern(string text)
    {
        return "^" + Regex.Escape(text.Trim()) + "$";
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
