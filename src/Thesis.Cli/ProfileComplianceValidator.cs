using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class ProfileComplianceValidator
{
    public static ValidationReport Validate(DocumentMap map, TemplateProfile profile)
    {
        profile.StyleRoles ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();

        var report = new ValidationReport
        {
            CheckedParagraphs = map.Paragraphs.Count,
            CheckedTables = map.Tables.Count
        };

        ValidatePageSetup(map, profile, report);
        ValidateStructure(map, profile, report);
        ValidateStylePolicy(map, profile, report);
        ValidatePackagePolicy(map, profile, report);
        ValidateFieldPolicy(map, profile, report);
        ValidateZonePolicy(map, profile, report);
        ValidateRoleEvidence(map, profile, report);
        ValidateTables(map, profile, report);
        report.Compliant = report.Diagnostics.Count == 0;
        return report;
    }

    private static void ValidatePageSetup(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var expected = profile.PageSetup;
        var hasExpectedPageSize = expected.PageSize is not null
            && (expected.PageSize.WidthTwips is not null
                || expected.PageSize.HeightTwips is not null
                || expected.PageSize.Orientation is not null);
        var hasExpectedMargins = expected.Margins is not null
            && (expected.Margins.TopTwips is not null
                || expected.Margins.RightTwips is not null
                || expected.Margins.BottomTwips is not null
                || expected.Margins.LeftTwips is not null
                || expected.Margins.HeaderTwips is not null
                || expected.Margins.FooterTwips is not null
                || expected.Margins.GutterTwips is not null);
        if (!hasExpectedPageSize && !hasExpectedMargins)
        {
            return;
        }

        var section = map.Sections.FirstOrDefault();
        if (section is null || !PageSizeMatches(section.PageSize, expected.PageSize) || !MarginsMatches(section.PageMargin, expected.Margins))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "profile_page_setup_mismatch",
                Message = "Document page setup does not match the template profile.",
                Path = "sections[0]"
            });
            report.SuggestedOperations.Add(new ThesisOperation
            {
                Id = "fix-page-setup",
                Op = "applyProfilePageSetup"
            });
        }
    }

    private static void ValidateStructure(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var expected = profile.StructurePolicy;
        if (expected.SectionCount > 0 && map.Sections.Count != expected.SectionCount)
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_section_count_mismatch",
                Message = $"Document has {map.Sections.Count} sections; profile expects {expected.SectionCount}.",
                Path = "sections"
            });
        }

        foreach (var expectedSection in expected.Sections)
        {
            var actual = map.Sections.FirstOrDefault(section => section.Index == expectedSection.Index);
            if (actual is null
                || !string.Equals(HeaderFooterSignature(actual.Headers), NormalizeHeaderFooterSignature(expectedSection.HeaderSignature), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(HeaderFooterSignature(actual.Footers), NormalizeHeaderFooterSignature(expectedSection.FooterSignature), StringComparison.OrdinalIgnoreCase))
            {
                report.Diagnostics.Add(new Diagnostic
                {
                    Severity = "error",
                    Code = "profile_section_header_footer_mismatch",
                    Message = $"Section {expectedSection.Index} header/footer topology does not match the profile.",
                    Path = $"sections[{expectedSection.Index}]"
                });
            }
        }
    }

    private static void ValidateStylePolicy(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var policy = profile.StylePolicy;
        if (policy.PreserveNumericStyleIds && policy.NumericStyleIds.Count > 0)
        {
            var actualNumericStyleIds = map.Styles
                .Select(style => style.StyleId)
                .Where(styleId => !string.IsNullOrWhiteSpace(styleId) && styleId.All(char.IsAsciiDigit))
                .Select(styleId => styleId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var expectedStyleId in policy.NumericStyleIds)
            {
                if (actualNumericStyleIds.Contains(expectedStyleId))
                {
                    continue;
                }

                report.Diagnostics.Add(new Diagnostic
                {
                    Severity = "error",
                    Code = "profile_numeric_style_missing",
                    Message = $"Profile expects numeric style id '{expectedStyleId}' to be preserved.",
                    Path = $"styles[{expectedStyleId}]"
                });
            }
        }

        if (policy.DisallowedGeneratedStyleIds.Count == 0)
        {
            return;
        }

        var usedStyleIds = map.Paragraphs
            .Select(paragraph => paragraph.StyleId)
            .Where(styleId => !string.IsNullOrWhiteSpace(styleId))
            .Select(styleId => styleId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var disallowedStyleId in policy.DisallowedGeneratedStyleIds.Where(usedStyleIds.Contains))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_disallowed_generated_style_used",
                Message = $"Generated style '{disallowedStyleId}' is not allowed by the profile.",
                Path = $"styles[{disallowedStyleId}]"
            });
        }
    }

    private static void ValidatePackagePolicy(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var policy = profile.PackagePolicy;
        if (!policy.AllowUnresolvedImageReferences && map.Package.UnresolvedImageReferenceCount > 0)
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_unresolved_image_reference",
                Message = $"Document contains {map.Package.UnresolvedImageReferenceCount} drawing image references without matching image relationships.",
                Path = "package.relationships"
            });
        }

        var imageRelationships = map.Package.Relationships
            .Where(relationship => string.Equals(relationship.Type, "image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (imageRelationships.Count == 0)
        {
            return;
        }

        if (string.Equals(policy.ImageRelationshipTargetMode, "relative", StringComparison.OrdinalIgnoreCase)
            && imageRelationships.Any(relationship =>
                !string.IsNullOrWhiteSpace(relationship.TargetMode)
                || relationship.Target.StartsWith("/", StringComparison.Ordinal)
                || relationship.Target.Contains("://", StringComparison.Ordinal)))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_image_relationship_target_invalid",
                Message = "Image relationships must use relative package targets.",
                Path = "package.relationships"
            });
        }

        if (string.Equals(policy.ImagePartRoot, "word/media", StringComparison.OrdinalIgnoreCase)
            && imageRelationships.Any(relationship => !relationship.Target.StartsWith("media/", StringComparison.Ordinal)))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_image_relationship_target_invalid",
                Message = "Image relationships must target files under word/media.",
                Path = "package.relationships"
            });
        }
    }

    private static void ValidateFieldPolicy(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var policy = profile.FieldPolicy;
        if (policy.RequiresToc
            && !map.FinalizationReasons.Contains("toc", StringComparer.OrdinalIgnoreCase)
            && !map.Package.FieldCodes.Any(field => string.Equals(field.Kind, "TOC", StringComparison.OrdinalIgnoreCase)))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_required_toc_missing",
                Message = "Profile requires a TOC field, but the document does not contain one.",
                Path = "fields"
            });
        }

        if (!policy.AllowTcFields
            && map.Package.FieldCodes.Any(field => string.Equals(field.Kind, "TC", StringComparison.OrdinalIgnoreCase)))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_tc_field_not_allowed",
                Message = "Profile does not allow TC fields in the final document.",
                Path = "fields"
            });
        }
    }

    private static void ValidateZonePolicy(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        if (profile.ZonePolicy.ForbiddenFrontMatterHeadings.Count == 0)
        {
            return;
        }

        foreach (var paragraph in map.Paragraphs)
        {
            var compactText = Compact(paragraph.Text);
            var forbidden = profile.ZonePolicy.ForbiddenFrontMatterHeadings
                .FirstOrDefault(heading => string.Equals(Compact(heading), compactText, StringComparison.Ordinal));
            if (forbidden is null)
            {
                continue;
            }

            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = "profile_forbidden_front_matter",
                Message = $"Document contains forbidden front-matter heading '{forbidden}'.",
                Path = $"paragraphs[{paragraph.Index}]"
            });
        }
    }

    private static void ValidateRoleEvidence(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        foreach (var role in profile.StyleRoles.Where(role => role.Format is not null))
        {
            var resolvedParagraphs = ResolveRoleParagraphs(map, profile, role).ToList();
            if (resolvedParagraphs.Count == 0)
            {
                if (IsOptionalAbsentRole(map, role.Role))
                {
                    continue;
                }

                report.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Code = "profile_role_target_unresolved",
                    Message = $"Role '{role.Role}' could not be resolved in the target document.",
                    Path = $"roles[{role.Role}]"
                });
                continue;
            }

            foreach (var paragraph in resolvedParagraphs)
            {
                if (ParagraphFormatMatches(paragraph.Format, role.Format!))
                {
                    continue;
                }

                report.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Code = "profile_role_format_mismatch",
                    Message = $"Paragraph {paragraph.Index} does not match profile role '{role.Role}'.",
                    Path = $"paragraphs[{paragraph.Index}]"
                });
                report.SuggestedOperations.Add(new ThesisOperation
                {
                    Id = $"fix-{SafeId(role.Role)}-{paragraph.Index}",
                    Op = "applyProfileRole",
                    Role = role.Role,
                    Target = new JsonObject
                    {
                        ["type"] = "paragraphIndex",
                        ["index"] = paragraph.Index
                    }
                });
            }
        }
    }

    private static IEnumerable<DocumentParagraph> ResolveRoleParagraphs(
        DocumentMap map,
        TemplateProfile profile,
        ProfileStyleRole role)
    {
        var policyMatches = ResolveRolePolicyParagraphs(map, profile, role.Role);
        if (policyMatches.Count > 0)
        {
            return policyMatches;
        }

        var semanticMatches = ResolveSemanticRoleParagraphs(map, role.Role);
        if (semanticMatches.Count > 0)
        {
            return semanticMatches;
        }

        return ResolveEvidenceParagraphs(map, profile, role);
    }

    private static List<DocumentParagraph> ResolveRolePolicyParagraphs(
        DocumentMap map,
        TemplateProfile profile,
        string role)
    {
        var policies = profile.RolePolicies
            .Where(policy =>
                RoleMatches(policy.Role, role)
                && string.Equals(policy.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(policy => policy.Priority)
            .ToList();
        if (policies.Count == 0)
        {
            return [];
        }

        try
        {
            return map.Paragraphs
                .Where(paragraph => policies.Any(policy => RolePolicyMatches(paragraph, policy)))
                .GroupBy(paragraph => paragraph.Index)
                .Select(group => group.First())
                .ToList();
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static List<DocumentParagraph> ResolveEvidenceParagraphs(
        DocumentMap map,
        TemplateProfile profile,
        ProfileStyleRole role)
    {
        var sameSourceDocument = SamePathOrEmpty(profile.SourceDocument, map.Path);
        return role.Evidence
            .Select(evidence => map.Paragraphs.FirstOrDefault(candidate => candidate.Index == evidence.ParagraphIndex))
            .Where(paragraph => paragraph is not null)
            .Select(paragraph => paragraph!)
            .Where(paragraph => sameSourceDocument
                || role.Evidence.Any(evidence =>
                    evidence.ParagraphIndex == paragraph.Index && EvidenceMatches(paragraph, evidence)))
            .GroupBy(paragraph => paragraph.Index)
            .Select(group => group.First())
            .ToList();
    }

    private static List<DocumentParagraph> ResolveSemanticRoleParagraphs(DocumentMap map, string role)
    {
        var predicate = ThesisTextHeuristics.SemanticRolePredicate(role);
        if (predicate is null)
        {
            return [];
        }

        return map.Paragraphs
            .Where(paragraph => !ThesisTextHeuristics.IsLikelyTocLine(paragraph.Text))
            .Where(paragraph => predicate(paragraph.Text))
            .ToList();
    }

    private static bool IsOptionalAbsentRole(DocumentMap map, string role)
    {
        return string.Equals(role, "appendix", StringComparison.OrdinalIgnoreCase)
            && ResolveSemanticRoleParagraphs(map, role).Count == 0;
    }

    private static bool RolePolicyMatches(DocumentParagraph paragraph, ProfileRolePolicy policy)
    {
        var match = policy.Match;
        return StyleMatches(paragraph, match.StyleIds)
            && TextPatternMatches(paragraph, match.TextPatterns)
            && OutlineLevelMatches(paragraph, match.OutlineLevels)
            && RoleFormatMatches(paragraph.Format, match.Format);
    }

    private static bool StyleMatches(DocumentParagraph paragraph, List<string> styleIds)
    {
        return styleIds.Count == 0
            || (paragraph.StyleId is not null
                && styleIds.Any(styleId => string.Equals(styleId, paragraph.StyleId, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TextPatternMatches(DocumentParagraph paragraph, List<string> textPatterns)
    {
        return textPatterns.Count == 0
            || textPatterns.Any(pattern => Regex.IsMatch(paragraph.Text, pattern, RegexOptions.CultureInvariant));
    }

    private static bool OutlineLevelMatches(DocumentParagraph paragraph, List<int> outlineLevels)
    {
        return outlineLevels.Count == 0
            || (paragraph.OutlineLevel is not null && outlineLevels.Contains(paragraph.OutlineLevel.Value));
    }

    private static bool RoleFormatMatches(ParagraphFormatSample actual, ProfileRoleFormatMatch? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return StringMatches(actual.StyleId, expected.StyleId)
            && StringMatches(actual.Alignment, expected.Alignment)
            && StringMatches(actual.RunFormat?.FontSizeHalfPoints, expected.FontSizeHalfPoints)
            && BoolMatches(actual.RunFormat?.Bold, expected.Bold)
            && BoolMatches(actual.RunFormat?.Italic, expected.Italic)
            && StringMatches(actual.LineSpacing, expected.LineSpacing)
            && StringMatches(actual.LineSpacingRule, expected.LineSpacingRule)
            && RangeMatches(actual.FirstLineIndentTwips, expected.FirstLineIndentTwips)
            && RangeMatches(actual.LeftIndentTwips, expected.LeftIndentTwips)
            && RangeMatches(actual.RightIndentTwips, expected.RightIndentTwips);
    }

    private static bool RangeMatches(int? actual, IntRangeMatch? expected)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        if (expected.Exact is not null)
        {
            return actual.Value == expected.Exact.Value;
        }

        if (expected.Min is not null && actual.Value < expected.Min.Value)
        {
            return false;
        }

        return expected.Max is null || actual.Value <= expected.Max.Value;
    }

    private static bool EvidenceMatches(DocumentParagraph paragraph, ProfileParagraphEvidence evidence)
    {
        if (!string.IsNullOrWhiteSpace(evidence.StyleId)
            && !string.Equals(paragraph.StyleId, evidence.StyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(evidence.TextPreview)
            || paragraph.Text.StartsWith(evidence.TextPreview, StringComparison.Ordinal);
    }

    private static bool RoleMatches(string candidate, string requested)
    {
        return string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)
            || IsTocAlias(candidate, requested);
    }

    private static bool IsTocAlias(string left, string right)
    {
        return string.Equals(NormalizeTocRole(left), NormalizeTocRole(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTocRole(string role)
    {
        return ThesisTextHeuristics.NormalizeTocRole(role);
    }

    private static bool SamePathOrEmpty(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateTables(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var profileFormat = profile.TablePolicy.Default?.Format;
        if (profileFormat is null)
        {
            return;
        }

        foreach (var table in map.Tables)
        {
            if (TableFormatMatches(table.Format, profileFormat))
            {
                continue;
            }

            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "profile_table_format_mismatch",
                Message = $"Table {table.Index} does not match the default profile table format.",
                Path = $"tables[{table.Index}]"
            });
            report.SuggestedOperations.Add(new ThesisOperation
            {
                Id = $"fix-table-{table.Index}",
                Op = "applyProfileTable",
                Target = new JsonObject
                {
                    ["type"] = "tableIndex",
                    ["index"] = table.Index
                }
            });
        }
    }

    private static bool ParagraphFormatMatches(ParagraphFormatSample actual, ParagraphFormatSample expected)
    {
        var directFormatMatches = StringMatches(actual.Alignment, expected.Alignment)
            && IntMatches(actual.SpacingBeforeTwips, expected.SpacingBeforeTwips)
            && IntMatches(actual.SpacingAfterTwips, expected.SpacingAfterTwips)
            && StringMatches(actual.LineSpacing, expected.LineSpacing)
            && StringMatches(actual.LineSpacingRule, expected.LineSpacingRule)
            && IntMatches(actual.FirstLineIndentTwips, expected.FirstLineIndentTwips)
            && IntMatches(actual.LeftIndentTwips, expected.LeftIndentTwips)
            && IntMatches(actual.RightIndentTwips, expected.RightIndentTwips)
            && RunFormatMatches(actual.RunFormat, expected.RunFormat);
        if (!directFormatMatches)
        {
            return false;
        }

        if (StringMatches(actual.StyleId, expected.StyleId))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(actual.StyleId)
            && !string.IsNullOrWhiteSpace(expected.StyleId)
            && HasStrongDirectParagraphFormatExpectation(expected);
    }

    private static bool RunFormatMatches(RunFormatSample? actual, RunFormatSample? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return BoolMatches(actual?.Bold, expected.Bold)
            && BoolMatches(actual?.Italic, expected.Italic)
            && StringMatches(actual?.FontSizeHalfPoints, expected.FontSizeHalfPoints)
            && StringMatches(actual?.AsciiFont, expected.AsciiFont)
            && StringMatches(actual?.HighAnsiFont, expected.HighAnsiFont)
            && StringMatches(actual?.EastAsiaFont, expected.EastAsiaFont)
            && StringMatches(actual?.ComplexScriptFont, expected.ComplexScriptFont);
    }

    private static bool HasStrongDirectParagraphFormatExpectation(ParagraphFormatSample expected)
    {
        var paragraphEvidenceCount = CountDirectParagraphFormatExpectations(expected);
        var runEvidenceCount = CountDirectRunFormatExpectations(expected.RunFormat);
        return paragraphEvidenceCount >= 1
            && runEvidenceCount >= 1
            && paragraphEvidenceCount + runEvidenceCount >= 3;
    }

    private static int CountDirectParagraphFormatExpectations(ParagraphFormatSample expected)
    {
        var count = 0;
        if (expected.Alignment is not null)
        {
            count++;
        }

        if (expected.SpacingBeforeTwips is not null)
        {
            count++;
        }

        if (expected.SpacingAfterTwips is not null)
        {
            count++;
        }

        if (expected.LineSpacing is not null)
        {
            count++;
        }

        if (expected.LineSpacingRule is not null)
        {
            count++;
        }

        if (expected.FirstLineIndentTwips is not null)
        {
            count++;
        }

        if (expected.LeftIndentTwips is not null)
        {
            count++;
        }

        if (expected.RightIndentTwips is not null)
        {
            count++;
        }

        return count;
    }

    private static int CountDirectRunFormatExpectations(RunFormatSample? expected)
    {
        if (expected is null)
        {
            return 0;
        }

        var count = 0;
        if (expected.Bold is not null)
        {
            count++;
        }

        if (expected.Italic is not null)
        {
            count++;
        }

        if (expected.FontSizeHalfPoints is not null)
        {
            count++;
        }

        if (expected.AsciiFont is not null)
        {
            count++;
        }

        if (expected.HighAnsiFont is not null)
        {
            count++;
        }

        if (expected.EastAsiaFont is not null)
        {
            count++;
        }

        if (expected.ComplexScriptFont is not null)
        {
            count++;
        }

        return count;
    }

    private static bool TableFormatMatches(TableFormatSample actual, TableFormatSample expected)
    {
        return IntMatches(actual.WidthTwips, expected.WidthTwips)
            && StringMatches(actual.WidthType, expected.WidthType)
            && StringMatches(actual.Alignment, expected.Alignment)
            && ListMatches(actual.GridColumnWidthsTwips, expected.GridColumnWidthsTwips)
            && actual.HeaderRowCount == expected.HeaderRowCount;
    }

    private static bool PageSizeMatches(PageSizeInfo? actual, PageSizeInfo? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return IntMatches(actual?.WidthTwips, expected.WidthTwips)
            && IntMatches(actual?.HeightTwips, expected.HeightTwips)
            && StringMatches(actual?.Orientation, expected.Orientation);
    }

    private static bool MarginsMatches(PageMarginInfo? actual, PageMarginInfo? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return IntMatches(actual?.TopTwips, expected.TopTwips)
            && IntMatches(actual?.RightTwips, expected.RightTwips)
            && IntMatches(actual?.BottomTwips, expected.BottomTwips)
            && IntMatches(actual?.LeftTwips, expected.LeftTwips)
            && IntMatches(actual?.HeaderTwips, expected.HeaderTwips)
            && IntMatches(actual?.FooterTwips, expected.FooterTwips)
            && IntMatches(actual?.GutterTwips, expected.GutterTwips);
    }

    private static bool StringMatches(string? actual, string? expected)
    {
        return expected is null || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IntMatches(int? actual, int? expected)
    {
        return expected is null || actual == expected;
    }

    private static bool BoolMatches(bool? actual, bool? expected)
    {
        return expected is null || actual == expected;
    }

    private static bool ListMatches(List<int> actual, List<int> expected)
    {
        return expected.Count == 0 || actual.SequenceEqual(expected);
    }

    private static string HeaderFooterSignature(List<HeaderFooterReference> references)
    {
        return string.Join(
            "|",
            references
                .Select(reference => Lower(reference.Type))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeHeaderFooterSignature(string signature)
    {
        return string.Join(
            "|",
            signature
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item =>
                {
                    var separator = item.IndexOf(':', StringComparison.Ordinal);
                    return Lower(separator >= 0 ? item[..separator] : item);
                })
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static string Lower(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.ToLowerInvariant();
    }

    private static string Compact(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", "", RegexOptions.CultureInvariant);
    }

    private static string SafeId(string value)
    {
        var chars = value
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-').ToLowerInvariant();
    }
}
