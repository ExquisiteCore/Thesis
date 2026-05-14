using Thesis.Schema;

namespace Thesis.Profile;

public static class ProjectRulesMerger
{
    public static TemplateProfile Merge(TemplateProfile profile, ProjectRules rules)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(rules);

        var merged = Clone(profile);
        ApplyRoleAliases(merged, rules);
        ApplyPageSetup(merged, rules.PageSetup);
        ApplyRoleFormats(merged, rules.RoleFormats);
        ApplyRolePolicies(merged, rules.RolePolicies);
        ApplyStructurePolicy(merged, rules.StructurePolicy);
        ApplyStylePolicy(merged, rules.StylePolicy);
        ApplyPackagePolicy(merged, rules.PackagePolicy);
        ApplyFieldPolicy(merged, rules.FieldPolicy);
        ApplyZonePolicy(merged, rules.ZonePolicy);
        ApplyTableRules(merged, rules);
        ApplyDiagnostics(merged, rules.Diagnostics);
        return merged;
    }

    private static TemplateProfile Clone(TemplateProfile profile)
    {
        return ThesisJson.Deserialize<TemplateProfile>(ThesisJson.Serialize(profile));
    }

    private static void ApplyRoleAliases(TemplateProfile profile, ProjectRules rules)
    {
        profile.RoleAliases ??= [];
        foreach (var (alias, role) in rules.RoleAliases)
        {
            var existing = profile.RoleAliases.FirstOrDefault(item =>
                string.Equals(item.Alias, alias, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                profile.RoleAliases.Add(new ProfileRoleAlias { Alias = alias, Role = role });
            }
            else
            {
                existing.Role = role;
            }
        }
    }

    private static void ApplyPageSetup(TemplateProfile profile, ProjectPageSetupRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.PageSetup ??= new ProfilePageSetup();
        if (rules.PageSize is not null)
        {
            profile.PageSetup.PageSize ??= new PageSizeInfo();
            ApplyPageSize(profile.PageSetup.PageSize, rules.PageSize);
        }

        if (rules.Margins is not null)
        {
            profile.PageSetup.Margins ??= new PageMarginInfo();
            ApplyMargins(profile.PageSetup.Margins, rules.Margins);
        }
    }

    private static void ApplyPageSize(PageSizeInfo target, PageSizeInfo source)
    {
        target.WidthTwips = source.WidthTwips ?? target.WidthTwips;
        target.HeightTwips = source.HeightTwips ?? target.HeightTwips;
        target.Orientation = source.Orientation ?? target.Orientation;
    }

    private static void ApplyMargins(PageMarginInfo target, PageMarginInfo source)
    {
        target.TopTwips = source.TopTwips ?? target.TopTwips;
        target.RightTwips = source.RightTwips ?? target.RightTwips;
        target.BottomTwips = source.BottomTwips ?? target.BottomTwips;
        target.LeftTwips = source.LeftTwips ?? target.LeftTwips;
        target.HeaderTwips = source.HeaderTwips ?? target.HeaderTwips;
        target.FooterTwips = source.FooterTwips ?? target.FooterTwips;
        target.GutterTwips = source.GutterTwips ?? target.GutterTwips;
    }

    private static void ApplyRoleFormats(TemplateProfile profile, Dictionary<string, ProjectParagraphFormatRule> roleFormats)
    {
        profile.StyleRoles ??= [];
        profile.RolePolicies ??= [];

        foreach (var (role, format) in roleFormats)
        {
            var targetRole = profile.StyleRoles.FirstOrDefault(item =>
                string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));
            if (targetRole is null)
            {
                targetRole = new ProfileStyleRole { Role = role, Confidence = 1, Evidence = [] };
                profile.StyleRoles.Add(targetRole);
            }

            var paragraphFormat = ToParagraphFormat(format);
            targetRole.Format ??= new ParagraphFormatSample();
            MergeParagraphFormat(targetRole.Format, paragraphFormat);

            var targetPolicy = profile.RolePolicies
                .Where(item => string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Priority)
                .FirstOrDefault();
            if (targetPolicy is not null)
            {
                targetPolicy.Format ??= new ParagraphFormatSample();
                MergeParagraphFormat(targetPolicy.Format, paragraphFormat);
                targetPolicy.Match ??= new ProfileRoleMatch();
                targetPolicy.Match.Format = ToRoleFormatMatch(paragraphFormat);
            }
            else
            {
                profile.RolePolicies.Add(new ProfileRolePolicy
                {
                    Role = role,
                    AppliesTo = "paragraph",
                    Priority = 200,
                    Confidence = 1,
                    Match = new ProfileRoleMatch
                    {
                        Format = ToRoleFormatMatch(paragraphFormat)
                    },
                    Format = CloneParagraphFormat(paragraphFormat)
                });
            }
        }
    }

    private static ParagraphFormatSample ToParagraphFormat(ProjectParagraphFormatRule rule)
    {
        var runFormat = rule.RunFormat is null ? null : CloneRunFormat(rule.RunFormat);
        if (rule.Bold is not null
            || rule.Italic is not null
            || rule.FontSizeHalfPoints is not null
            || rule.AsciiFont is not null
            || rule.HighAnsiFont is not null
            || rule.EastAsiaFont is not null
            || rule.ComplexScriptFont is not null)
        {
            runFormat ??= new RunFormatSample();
            runFormat.Bold = rule.Bold ?? runFormat.Bold;
            runFormat.Italic = rule.Italic ?? runFormat.Italic;
            runFormat.FontSizeHalfPoints = rule.FontSizeHalfPoints ?? runFormat.FontSizeHalfPoints;
            runFormat.AsciiFont = rule.AsciiFont ?? runFormat.AsciiFont;
            runFormat.HighAnsiFont = rule.HighAnsiFont ?? runFormat.HighAnsiFont;
            runFormat.EastAsiaFont = rule.EastAsiaFont ?? runFormat.EastAsiaFont;
            runFormat.ComplexScriptFont = rule.ComplexScriptFont ?? runFormat.ComplexScriptFont;
        }

        return new ParagraphFormatSample
        {
            StyleId = rule.StyleId,
            Alignment = rule.Alignment,
            SpacingBeforeTwips = rule.SpacingBeforeTwips,
            SpacingAfterTwips = rule.SpacingAfterTwips,
            LineSpacing = rule.LineSpacing,
            LineSpacingRule = rule.LineSpacingRule,
            FirstLineIndentTwips = rule.FirstLineIndentTwips,
            LeftIndentTwips = rule.LeftIndentTwips,
            RightIndentTwips = rule.RightIndentTwips,
            RunFormat = runFormat
        };
    }

    private static RunFormatSample CloneRunFormat(RunFormatSample source)
    {
        return new RunFormatSample
        {
            Bold = source.Bold,
            Italic = source.Italic,
            FontSizeHalfPoints = source.FontSizeHalfPoints,
            AsciiFont = source.AsciiFont,
            HighAnsiFont = source.HighAnsiFont,
            EastAsiaFont = source.EastAsiaFont,
            ComplexScriptFont = source.ComplexScriptFont
        };
    }

    private static ParagraphFormatSample CloneParagraphFormat(ParagraphFormatSample source)
    {
        return new ParagraphFormatSample
        {
            StyleId = source.StyleId,
            Alignment = source.Alignment,
            SpacingBeforeTwips = source.SpacingBeforeTwips,
            SpacingAfterTwips = source.SpacingAfterTwips,
            LineSpacing = source.LineSpacing,
            LineSpacingRule = source.LineSpacingRule,
            FirstLineIndentTwips = source.FirstLineIndentTwips,
            LeftIndentTwips = source.LeftIndentTwips,
            RightIndentTwips = source.RightIndentTwips,
            RunFormat = source.RunFormat is null ? null : CloneRunFormat(source.RunFormat)
        };
    }

    private static ProfileRoleFormatMatch ToRoleFormatMatch(ParagraphFormatSample format)
    {
        return new ProfileRoleFormatMatch
        {
            StyleId = format.StyleId,
            Alignment = format.Alignment,
            FontSizeHalfPoints = format.RunFormat?.FontSizeHalfPoints,
            Bold = format.RunFormat?.Bold,
            Italic = format.RunFormat?.Italic,
            LineSpacing = format.LineSpacing,
            LineSpacingRule = format.LineSpacingRule,
            FirstLineIndentTwips = ToExactRange(format.FirstLineIndentTwips),
            LeftIndentTwips = ToExactRange(format.LeftIndentTwips),
            RightIndentTwips = ToExactRange(format.RightIndentTwips)
        };
    }

    private static IntRangeMatch? ToExactRange(int? value)
    {
        return value is null ? null : new IntRangeMatch { Exact = value };
    }

    private static void ApplyRolePolicies(TemplateProfile profile, List<ProfileRolePolicy> policies)
    {
        profile.RolePolicies ??= [];
        foreach (var policy in policies)
        {
            var existingIndex = profile.RolePolicies.FindIndex(item =>
                string.Equals(item.Role, policy.Role, StringComparison.OrdinalIgnoreCase)
                && item.Priority == policy.Priority);
            if (existingIndex >= 0)
            {
                profile.RolePolicies[existingIndex] = policy;
            }
            else
            {
                profile.RolePolicies.Add(policy);
            }
        }
    }

    private static void ApplyStructurePolicy(TemplateProfile profile, ProjectStructurePolicyRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.StructurePolicy ??= new ProfileStructurePolicy();
        profile.StructurePolicy.SectionCount = rules.SectionCount ?? profile.StructurePolicy.SectionCount;
        if (rules.Sections is not null)
        {
            profile.StructurePolicy.Sections = [.. rules.Sections.Select(CloneSectionSignature)];
        }
    }

    private static ProfileSectionSignature CloneSectionSignature(ProfileSectionSignature source)
    {
        return new ProfileSectionSignature
        {
            Index = source.Index,
            HeaderSignature = source.HeaderSignature,
            FooterSignature = source.FooterSignature,
            PageSize = source.PageSize is null ? null : ClonePageSize(source.PageSize),
            Margins = source.Margins is null ? null : CloneMargins(source.Margins)
        };
    }

    private static PageSizeInfo ClonePageSize(PageSizeInfo source)
    {
        return new PageSizeInfo
        {
            WidthTwips = source.WidthTwips,
            HeightTwips = source.HeightTwips,
            Orientation = source.Orientation
        };
    }

    private static PageMarginInfo CloneMargins(PageMarginInfo source)
    {
        return new PageMarginInfo
        {
            TopTwips = source.TopTwips,
            RightTwips = source.RightTwips,
            BottomTwips = source.BottomTwips,
            LeftTwips = source.LeftTwips,
            HeaderTwips = source.HeaderTwips,
            FooterTwips = source.FooterTwips,
            GutterTwips = source.GutterTwips
        };
    }

    private static void ApplyStylePolicy(TemplateProfile profile, ProjectStylePolicyRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.StylePolicy ??= new ProfileStylePolicy();
        profile.StylePolicy.PreserveNumericStyleIds = rules.PreserveNumericStyleIds ?? profile.StylePolicy.PreserveNumericStyleIds;
        if (rules.NumericStyleIds is not null)
        {
            profile.StylePolicy.NumericStyleIds = DistinctNonEmpty(rules.NumericStyleIds);
        }

        if (rules.DisallowedGeneratedStyleIds is not null)
        {
            profile.StylePolicy.DisallowedGeneratedStyleIds = DistinctNonEmpty(rules.DisallowedGeneratedStyleIds);
        }
    }

    private static void ApplyPackagePolicy(TemplateProfile profile, ProjectPackagePolicyRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.PackagePolicy ??= new ProfilePackagePolicy();
        profile.PackagePolicy.ImagePartRoot = rules.ImagePartRoot ?? profile.PackagePolicy.ImagePartRoot;
        profile.PackagePolicy.ImageRelationshipTargetMode = rules.ImageRelationshipTargetMode ?? profile.PackagePolicy.ImageRelationshipTargetMode;
        profile.PackagePolicy.ImageCount = rules.ImageCount ?? profile.PackagePolicy.ImageCount;
        profile.PackagePolicy.AllowUnresolvedImageReferences = rules.AllowUnresolvedImageReferences ?? profile.PackagePolicy.AllowUnresolvedImageReferences;
    }

    private static void ApplyFieldPolicy(TemplateProfile profile, ProjectFieldPolicyRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.FieldPolicy ??= new ProfileFieldPolicy();
        profile.FieldPolicy.RequiresToc = rules.RequiresToc ?? profile.FieldPolicy.RequiresToc;
        profile.FieldPolicy.AllowTcFields = rules.AllowTcFields ?? profile.FieldPolicy.AllowTcFields;
    }

    private static void ApplyZonePolicy(TemplateProfile profile, ProjectZonePolicyRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        profile.ZonePolicy ??= new ProfileZonePolicy();
        if (rules.Landmarks is not null)
        {
            profile.ZonePolicy.Landmarks = [.. rules.Landmarks.Select(CloneZoneLandmark)];
        }

        if (rules.ForbiddenFrontMatterHeadings is not null)
        {
            profile.ZonePolicy.ForbiddenFrontMatterHeadings = DistinctNonEmpty(rules.ForbiddenFrontMatterHeadings);
        }
    }

    private static ProfileZoneLandmark CloneZoneLandmark(ProfileZoneLandmark source)
    {
        return new ProfileZoneLandmark
        {
            Role = source.Role,
            ParagraphIndex = source.ParagraphIndex,
            BodyElementIndex = source.BodyElementIndex,
            TextPreview = source.TextPreview
        };
    }

    private static List<string> DistinctNonEmpty(List<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyTableRules(TemplateProfile profile, ProjectRules rules)
    {
        profile.TablePolicy ??= new ProfileTablePolicy();
        if (rules.TableDefault is not null)
        {
            profile.TablePolicy.Default ??= new ProfileTableSample { Format = new TableFormatSample() };
            profile.TablePolicy.Default.Format ??= new TableFormatSample();
            MergeTableFormat(profile.TablePolicy.Default.Format, rules.TableDefault);
        }

        profile.TableArchetypes ??= [];
        foreach (var archetype in rules.TableArchetypes)
        {
            var existingIndex = profile.TableArchetypes.FindIndex(item =>
                string.Equals(item.Name, archetype.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                profile.TableArchetypes[existingIndex] = archetype;
            }
            else
            {
                profile.TableArchetypes.Add(archetype);
            }
        }
    }

    private static void ApplyDiagnostics(TemplateProfile profile, List<ProfileDiagnostic> diagnostics)
    {
        profile.Diagnostics ??= [];
        profile.Diagnostics.AddRange(diagnostics);
    }

    private static void MergeParagraphFormat(ParagraphFormatSample target, ParagraphFormatSample source)
    {
        target.StyleId = source.StyleId ?? target.StyleId;
        target.Alignment = source.Alignment ?? target.Alignment;
        target.SpacingBeforeTwips = source.SpacingBeforeTwips ?? target.SpacingBeforeTwips;
        target.SpacingAfterTwips = source.SpacingAfterTwips ?? target.SpacingAfterTwips;
        target.LineSpacing = source.LineSpacing ?? target.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule ?? target.LineSpacingRule;
        target.FirstLineIndentTwips = source.FirstLineIndentTwips ?? target.FirstLineIndentTwips;
        target.LeftIndentTwips = source.LeftIndentTwips ?? target.LeftIndentTwips;
        target.RightIndentTwips = source.RightIndentTwips ?? target.RightIndentTwips;

        if (source.RunFormat is not null)
        {
            target.RunFormat ??= new RunFormatSample();
            MergeRunFormat(target.RunFormat, source.RunFormat);
        }
    }

    private static void MergeRunFormat(RunFormatSample target, RunFormatSample source)
    {
        target.Bold = source.Bold ?? target.Bold;
        target.Italic = source.Italic ?? target.Italic;
        target.FontSizeHalfPoints = source.FontSizeHalfPoints ?? target.FontSizeHalfPoints;
        target.AsciiFont = source.AsciiFont ?? target.AsciiFont;
        target.HighAnsiFont = source.HighAnsiFont ?? target.HighAnsiFont;
        target.EastAsiaFont = source.EastAsiaFont ?? target.EastAsiaFont;
        target.ComplexScriptFont = source.ComplexScriptFont ?? target.ComplexScriptFont;
    }

    private static void MergeTableFormat(TableFormatSample target, TableFormatSample source)
    {
        target.WidthTwips = source.WidthTwips ?? target.WidthTwips;
        target.WidthType = source.WidthType ?? target.WidthType;
        target.Alignment = source.Alignment ?? target.Alignment;
        if (source.GridColumnWidthsTwips.Count > 0)
        {
            target.GridColumnWidthsTwips = [.. source.GridColumnWidthsTwips];
        }

        if (source.Borders is not null)
        {
            target.Borders ??= new TableBordersSample();
            MergeBorders(target.Borders, source.Borders);
        }

        if (source.CellMargins is not null)
        {
            target.CellMargins ??= new TableCellMarginsSample();
            MergeCellMargins(target.CellMargins, source.CellMargins);
        }

        if (source.HeaderRowCount > 0)
        {
            target.HeaderRowCount = source.HeaderRowCount;
        }
    }

    private static void MergeBorders(TableBordersSample target, TableBordersSample source)
    {
        target.Top = source.Top ?? target.Top;
        target.Bottom = source.Bottom ?? target.Bottom;
        target.Left = source.Left ?? target.Left;
        target.Right = source.Right ?? target.Right;
        target.InsideHorizontal = source.InsideHorizontal ?? target.InsideHorizontal;
        target.InsideVertical = source.InsideVertical ?? target.InsideVertical;
    }

    private static void MergeCellMargins(TableCellMarginsSample target, TableCellMarginsSample source)
    {
        target.TopTwips = source.TopTwips ?? target.TopTwips;
        target.RightTwips = source.RightTwips ?? target.RightTwips;
        target.BottomTwips = source.BottomTwips ?? target.BottomTwips;
        target.LeftTwips = source.LeftTwips ?? target.LeftTwips;
    }
}
